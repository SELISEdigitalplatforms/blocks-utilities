using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns a chosen plan and price into a stored subscription.
/// </summary>
public sealed class SubscriptionCreationService : ISubscriptionCreationService
{
    private const string StripeProvider = PaymentConstants.StripeProvider;

    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IValidator<CreateSubscriptionRequest> _validator;
    private readonly ILogger<SubscriptionCreationService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCreationService(
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        IValidator<CreateSubscriptionRequest> validator,
        ILogger<SubscriptionCreationService> logger,
        TimeProvider? time = null)
    {
        _catalogue = catalogue;
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var invalid = await SubscriptionValidation
            .CheckAsync<CreateSubscriptionRequest, SubscriptionDetail>(
                _validator,
                request,
                "subscription_request_invalid",
                "The subscription request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var terms = await ResolveTermsAsync(request, context, correlationId, cancellationToken);

        if (!terms.IsSuccess)
        {
            return terms.ToFailure<SubscriptionDetail>();
        }

        var (plan, price) = terms.Value;
        var quantities = BuildQuantities(request, plan, price);

        if (quantities is null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The quantities do not match the plan's items or fall outside their bounds.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        if (!BillingPeriodCalculator.TryCreateSchedule(
                price.Interval,
                price.IntervalCount,
                now,
                request.TimeZoneId,
                out var feeSchedule) ||
            !BillingPeriodCalculator.TryCreateSchedule(
                BillingInterval.Month,
                1,
                now,
                request.TimeZoneId,
                out var usageSchedule))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The billing schedule could not be derived from this time zone.",
                correlationId);
        }

        var account = await _billingAccounts.GetOrCreateAsync(
            new BillingAccount
            {
                TenantId = context.TenantId,
                OrganizationId = context.OrganizationId,
                ProviderName = StripeProvider,
                BillingEmail = request.BillingEmail,
                BillingName = request.BillingName
            },
            cancellationToken);

        var subscription = BuildSubscription(
            context,
            plan,
            price,
            quantities,
            account,
            feeSchedule,
            usageSchedule,
            now,
            correlationId);

        if (!ApplyPeriods(subscription, now))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The billing periods could not be computed for this schedule.",
                correlationId);
        }

        if (!await _subscriptions.TryCreateAsync(subscription, cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_already_active",
                "This organization already has a live subscription.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription created TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
            "SubscriptionHash={SubscriptionHash} Plan={Plan} Status={Status} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(plan.Code),
            PaymentLogValue.Label(subscription.Status.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionDetail>.Success(
            subscription,
            correlationId);
    }

    private async Task<SubscriptionOperationResult<(Plan Plan, Price Price)>> ResolveTermsAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var plan = await _catalogue.FindPlanByCodeAsync(
            context.TenantId,
            context.OrganizationId,
            request.PlanCode,
            cancellationToken);

        if (plan is null)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_plan_not_found",
                "The plan does not exist or is not on sale.",
                correlationId);
        }

        var price = await _catalogue.GetPriceAsync(
            context.TenantId,
            request.PriceId,
            cancellationToken);

        if (price is null ||
            !string.Equals(price.PlanId, plan.ItemId, StringComparison.Ordinal) ||
            price.Status != CatalogueStatus.Active)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_price_not_found",
                "The price does not exist for this plan.",
                correlationId);
        }

        return SubscriptionOperationResult<(Plan, Price)>.Success(
            (plan, price),
            correlationId);
    }

    /// <summary>
    /// Matches requested quantities to the plan's items, filling in defaults for anything the
    /// caller left out and refusing anything outside the plan's bounds.
    /// </summary>
    private static List<SubscriptionQuantityItem>? BuildQuantities(
        CreateSubscriptionRequest request,
        Plan plan,
        Price price)
    {
        var requested = request.Quantities.ToDictionary(
            quantity => quantity.ItemKey,
            quantity => quantity.Quantity,
            StringComparer.Ordinal);

        if (requested.Keys.Any(key =>
                !plan.QuantityItems.Exists(item =>
                    string.Equals(item.ItemKey, key, StringComparison.Ordinal))))
        {
            return null;
        }

        var items = new List<SubscriptionQuantityItem>(plan.QuantityItems.Count);

        foreach (var item in plan.QuantityItems)
        {
            var quantity = requested.TryGetValue(item.ItemKey, out var supplied)
                ? supplied
                : item.DefaultQuantity;

            if (quantity < item.MinQuantity ||
                (item.MaxQuantity is { } maximum && quantity > maximum))
            {
                return null;
            }

            items.Add(new SubscriptionQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                Quantity = quantity,
                // Snapshotted so a later catalogue edit cannot move what this subscriber pays.
                UnitAmountMinor = string.Equals(
                    item.ItemKey,
                    price.QuantityItemKey,
                    StringComparison.Ordinal)
                    ? price.UnitAmountMinor
                    : 0
            });
        }

        return items;
    }

    private static SubscriptionDetail BuildSubscription(
        SubscriptionContext context,
        Plan plan,
        Price price,
        List<SubscriptionQuantityItem> quantities,
        BillingAccount account,
        BillingSchedule feeSchedule,
        BillingSchedule usageSchedule,
        DateTime now,
        string correlationId)
    {
        var subscriptionId = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = subscriptionId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            BillingAccountId = account.ItemId,
            Status = SubscriptionStatus.Incomplete,
            CurrencyCode = price.CurrencyCode,
            Plan = SnapshotOf(plan),
            Price = SnapshotOf(price),
            QuantityItems = quantities,
            FeeSchedule = feeSchedule,
            UsageSchedule = usageSchedule,
            Trial = BuildTrial(plan, now),
            OrderId = SubscriptionConstants.OrderIdFor(subscriptionId),
            CorrelationId = correlationId,
            CreatedAtUtc = now,
            LastUpdatedDateUtc = now
        };
    }

    private static TrialTerms? BuildTrial(Plan plan, DateTime now) =>
        plan.TrialDays is not { } days
            ? null
            : new TrialTerms
            {
                StartsAtUtc = now,
                EndsAtUtc = now.AddDays(days),
                RequiresPaymentMethod = plan.TrialRequiresPaymentMethod,
                Grants = plan.TrialGrants
                    .Select(grant => new TrialMeterGrant
                    {
                        MeterKey = grant.MeterKey,
                        IncludedQuantity = grant.IncludedQuantity
                    })
                    .ToList()
            };

    private static bool ApplyPeriods(SubscriptionDetail subscription, DateTime now)
    {
        if (!BillingPeriodCalculator.TryGetPeriod(
                subscription.FeeSchedule,
                now,
                out var feePeriod) ||
            !BillingPeriodCalculator.TryGetPeriod(
                subscription.UsageSchedule,
                now,
                out var usagePeriod))
        {
            return false;
        }

        subscription.CurrentPeriodStartUtc = feePeriod.StartUtc;
        subscription.CurrentPeriodEndUtc = feePeriod.EndUtc;
        subscription.NextFeeBillingAtUtc = subscription.Trial?.EndsAtUtc ?? feePeriod.EndUtc;
        subscription.CurrentUsagePeriodStartUtc = usagePeriod.StartUtc;
        subscription.CurrentUsagePeriodEndUtc = usagePeriod.EndUtc;
        subscription.NextUsageBillingAtUtc = usagePeriod.EndUtc;

        return true;
    }

    /// <summary>
    /// Copies the plan's terms onto the subscription. A copy, not a reference: editing the
    /// catalogue afterwards must not change what an existing subscriber is entitled to or
    /// charged.
    /// </summary>
    private static PlanSnapshot SnapshotOf(Plan plan) => new()
    {
        PlanId = plan.ItemId,
        Code = plan.Code,
        DisplayName = plan.DisplayName,
        FeaturesJson = plan.FeaturesJson,
        PlanVersion = plan.Version,
        Entitlements = plan.Entitlements
            .Select(entitlement => new PlanEntitlement
            {
                Key = entitlement.Key,
                LimitKind = entitlement.LimitKind,
                Limit = entitlement.Limit,
                MeterKey = entitlement.MeterKey,
                UnitLabel = entitlement.UnitLabel
            })
            .ToList(),
        Meters = plan.Meters
            .Select(meter => new PlanMeter
            {
                MeterKey = meter.MeterKey,
                DisplayName = meter.DisplayName,
                UnitLabel = meter.UnitLabel,
                Aggregation = meter.Aggregation,
                IncludedQuantity = meter.IncludedQuantity,
                OverageAllowed = meter.OverageAllowed,
                ThresholdPercents = [.. meter.ThresholdPercents],
                RateTables = meter.RateTables
                    .Select(table => new MeterRateTable
                    {
                        CurrencyCode = table.CurrencyCode,
                        Tiers = table.Tiers
                            .Select(tier => new MeterTier
                            {
                                UpToQuantity = tier.UpToQuantity,
                                UnitAmountMinor = tier.UnitAmountMinor
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList(),
        QuantityItems = plan.QuantityItems
            .Select(item => new PlanQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                MinQuantity = item.MinQuantity,
                MaxQuantity = item.MaxQuantity,
                DefaultQuantity = item.DefaultQuantity
            })
            .ToList()
    };

    private static PriceSnapshot SnapshotOf(Price price) => new()
    {
        PriceId = price.ItemId,
        CurrencyCode = price.CurrencyCode,
        UnitAmountMinor = price.UnitAmountMinor,
        Interval = price.Interval,
        IntervalCount = price.IntervalCount,
        QuantityItemKey = price.QuantityItemKey,
        PriceVersion = price.Version
    };

    private static SubscriptionOperationResult<SubscriptionDetail> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionDetail>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
