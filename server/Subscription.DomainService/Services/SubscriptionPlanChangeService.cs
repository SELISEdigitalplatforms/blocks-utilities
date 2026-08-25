using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Moves a live subscription to a different price, mid-period.
/// </summary>
/// <remarks>
/// A trial has paid nothing yet, so its plan simply swaps. Otherwise the change is priced by
/// <see cref="SubscriptionProrationCalculator"/> and, if that leaves something owing, charged
/// immediately through the same <see cref="ISubscriptionBillingGateway"/> a renewal uses — this
/// is the seam's third caller, after the renewal service and (indirectly) the checkout service's
/// sibling money path.
/// <para>
/// The target cadence starts at the change instant. Both fee and usage schedules are rebuilt so
/// changing a monthly subscription to annual cannot leave a monthly renewal clock behind.
/// </para>
/// </remarks>
public sealed class SubscriptionPlanChangeService : ISubscriptionPlanChangeService
{
    /// <summary>
    /// Failures that state plainly that no money moved, and so may release the reservation.
    /// </summary>
    /// <remarks>
    /// Everything absent from this list is an <em>unanswered</em> charge rather than a declined one.
    /// The provider may have collected and lost the reply, and releasing on those would let a retry
    /// reserve again, charge again, and take the money twice.
    /// </remarks>
    private static readonly PaymentFailureKind[] SettledFailureKinds =
    [
        PaymentFailureKind.ProviderRejected,
        PaymentFailureKind.Validation,
        PaymentFailureKind.NotFound,
        PaymentFailureKind.Conflict,
        PaymentFailureKind.RateLimited
    ];

    private static readonly SubscriptionStatus[] EligibleStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active
    ];

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IValidator<ChangeSubscriptionPlanRequest> _validator;
    private readonly ILogger<SubscriptionPlanChangeService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionPlanChangeService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        ISubscriptionCatalogueRepository catalogue,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        ISubscriptionResponseMapper mapper,
        IValidator<ChangeSubscriptionPlanRequest> validator,
        ILogger<SubscriptionPlanChangeService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null)
    {
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _catalogue = catalogue;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _scheduler = scheduler;
        _mapper = mapper;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> ChangePlanAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation
            .CheckAsync<ChangeSubscriptionPlanRequest, SubscriptionResponse>(
                _validator,
                request,
                "subscription_plan_change_invalid",
                "The plan change request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        if (!EligibleStatuses.Contains(subscription.Status))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_not_eligible",
                "This subscription cannot change plan in its current state.",
                correlationId);
        }

        // A quantity increase is holding units priced against the plan being left. Refused by name
        // rather than by the repository's filter alone, so the caller knows to re-read and retry
        // rather than reading it as a stale version.
        if (subscription.SettlementReservation is not null)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_quantity_change_in_flight",
                "A quantity change is being settled on this subscription.",
                correlationId);
        }

        var terms = await ResolveTargetAsync(request, context, subscription, correlationId, cancellationToken);

        if (!terms.IsSuccess)
        {
            return terms.ToFailure<SubscriptionResponse>();
        }

        var (plan, price) = terms.Value;

        if (!DiscountSurvives(subscription, plan, price))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_discount_not_applicable",
                "The discount on this subscription does not apply to the plan or price being "
                    + "moved to. Cancel the subscription and start a new one to change onto it, "
                    + "or choose a target the discount covers.",
                correlationId);
        }

        var quantities = SubscriptionQuantityBuilder.Build(request.Quantities, plan, price);

        if (quantities is null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The quantities do not match the plan's items or fall outside their bounds.",
                correlationId);
        }

        var newPlan = SubscriptionSnapshotBuilder.SnapshotOf(plan);
        var newPrice = SubscriptionSnapshotBuilder.SnapshotOf(price);
        var now = _time.GetUtcNow().UtcDateTime;

        if (!TryBuildSchedule(subscription, newPlan, newPrice, now, out var newSchedule))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The target plan's schedules could not be derived.",
                correlationId);
        }

        return subscription.Status == SubscriptionStatus.Trialing
            ? await ApplyAsync(
                subscription, newPlan, newPrice, quantities, newSchedule,
                subscription.CreditBalanceMinor, null, null, correlationId, cancellationToken)
            : await ChargeAndApplyAsync(
                subscription, newPlan, newPrice, quantities, newSchedule, now,
                correlationId, cancellationToken);
    }

    /// <summary>
    /// Whether the subscriber's promotional discount may follow them to the target plan and price.
    /// </summary>
    /// <remarks>
    /// A plan change keeps the subscription's discount — that is the point of snapshotting it — but a
    /// code authored for the monthly price must not end up reducing the annual one. The restriction
    /// is read from the terms copied at redemption rather than from the catalogue, so a discount
    /// retired or re-scoped since then is judged by the offer the subscriber actually accepted.
    /// <para>
    /// Refused rather than silently dropped. Removing the promotion would change what the subscriber
    /// pays every period from here on, and doing that inside an operation they asked for a
    /// <em>price</em> quote on is the kind of surprise that shows up as a support ticket months
    /// later. Refusing puts the consequence in front of them first.
    /// </para>
    /// <para>
    /// Only asked of a discount that is still reducing charges. One whose duration is spent or whose
    /// expiry has passed reduces nothing, so blocking a plan change over its restrictions would be
    /// enforcing an offer that has already ended.
    /// </para>
    /// </remarks>
    private bool DiscountSurvives(SubscriptionDetail subscription, Plan plan, Price price)
    {
        if (subscription.Discount is not { } discount ||
            !SubscriptionAmountCalculator.DiscountStillActive(
                discount,
                subscription.DiscountPeriodsApplied,
                _time.GetUtcNow().UtcDateTime))
        {
            return true;
        }

        return SubscriptionDiscountApplicability.Permits(discount, plan.Code, price.ItemId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAndApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        SubscriptionPlanSchedule newSchedule,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            newPlan,
            newPrice,
            quantities,
            now,
            newSchedule.CurrentPeriodStartUtc,
            newSchedule.CurrentPeriodEndUtc,
            newSchedule.FeePeriodFraction);

        if (outcome.ChargeMinor <= 0)
        {
            return await ApplyAsync(
                subscription, newPlan, newPrice, quantities, newSchedule,
                outcome.NewCreditBalanceMinor, null, null, correlationId, cancellationToken);
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_no_payment_method",
                "This upgrade cannot be charged without a saved payment method.",
                correlationId);
        }

        // Reserved before anything is spent, and the charge is keyed on the reservation rather than
        // on the version. Keyed on the version, a write lost to a concurrent change left the money
        // moved and the plan unchanged, and the retry — which necessarily read a new version — built
        // a different key and charged again.
        var reservation = new SettlementReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            Kind = SettlementReservationKind.PlanChange,
            PlanChange = new ReservedPlanChange
            {
                Plan = newPlan,
                Price = newPrice,
                QuantityItems = quantities,
                Schedule = newSchedule,
                OutgoingUsagePeriod = SnapshotOutgoingUsagePeriod(subscription, correlationId),
                NewCreditBalanceMinor = outcome.NewCreditBalanceMinor
            },
            ChargeAmountMinor = outcome.ChargeMinor,
            Settlement = SettlementCharge.BreakdownOf(outcome),
            BillingAccountId = subscription.BillingAccountId,
            ProviderName = account.ProviderName,
            ProviderOrganizationId = account.ProviderOrganizationId,
            ProviderCustomerId = account.ProviderCustomerId,
            StoredPaymentMethodId = account.DefaultPaymentMethodId,
            ReservedAtUtc = now,
            CorrelationId = correlationId,
            ReservedAtVersion = subscription.Version
        };

        if (!await _subscriptions.TryReserveSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                reservation,
                cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
        }

        // Announced before the charge, so a reservation that is written and then stranded by a
        // dying process is already known about. Best effort inside the scheduler: this must not be
        // able to fail the change it is announcing.
        if (_scheduler is not null)
        {
            await _scheduler.ScheduleReservationRecoveryAsync(
                subscription, reservation, correlationId, cancellationToken);
        }

        var charge = await _gateway.ChargeAsync(
            SettlementCharge.RequestFor(subscription, reservation),
            SettlementCharge.KeyFor(subscription, reservation),
            correlationId,
            cancellationToken);

        if (!charge.IsSuccess)
        {
            _logger.LogWarning(
                "Subscription plan change was not charged TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Kind={Kind} Reason={Reason}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                charge.FailureKind,
                PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

            if (!SettledFailureKinds.Contains(charge.FailureKind))
            {
                // Nobody knows whether the money moved. The reservation stays, so a retry is
                // refused rather than charging again, and the sweep resolves it by asking the
                // payment module what the provider actually did.
                _logger.LogError(
                    "A subscription plan change left its charge unanswered and is held for " +
                    "reconciliation SubscriptionHash={SubscriptionHash} Kind={Kind}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    charge.FailureKind);

                return Failure(
                    charge.FailureKind,
                    "subscription_plan_change_charge_unresolved",
                    "The charge for this plan change could not be confirmed. " +
                    "Re-read the subscription before trying again.",
                    correlationId);
            }

            await ReleaseAsync(subscription, reservation, cancellationToken);

            return charge.ToFailure<SubscriptionResponse>();
        }

        return await ApplyAsync(
            subscription, newPlan, newPrice, quantities, newSchedule,
            outcome.NewCreditBalanceMinor, charge.Value, reservation.ReservationId,
            correlationId, cancellationToken);
    }


    private async Task ReleaseAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        CancellationToken cancellationToken)
    {
        if (await _subscriptions.TryReleaseSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                reservation.ReservationId,
                cancellationToken))
        {
            return;
        }

        // Left for the sweep rather than retried here: it asks the payment module what became of
        // the charge, which is the only thing that can tell a lost release from a settled one.
        _logger.LogError(
            "A declined subscription plan change could not release its reservation " +
            "SubscriptionHash={SubscriptionHash}",
            PaymentLogValue.Hash(subscription.ItemId));
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        SubscriptionPlanSchedule newSchedule,
        long newCreditBalanceMinor,
        string? paymentDetailId,
        string? reservationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var previousPlanCode = subscription.Plan.Code;
        var expectedVersion = subscription.Version;
        var outgoingUsagePeriod = SnapshotOutgoingUsagePeriod(subscription, correlationId);

        subscription.Plan = newPlan;
        subscription.Price = newPrice;
        subscription.QuantityItems = quantities;
        subscription.FeeSchedule = newSchedule.FeeSchedule;
        subscription.CurrentPeriodStartUtc = newSchedule.CurrentPeriodStartUtc;
        subscription.CurrentPeriodEndUtc = newSchedule.CurrentPeriodEndUtc;
        subscription.NextFeeBillingAtUtc = newSchedule.NextFeeBillingAtUtc;
        subscription.UsageSchedule = newSchedule.UsageSchedule;
        subscription.CurrentUsagePeriodStartUtc = newSchedule.CurrentUsagePeriodStartUtc;
        subscription.CurrentUsagePeriodEndUtc = newSchedule.CurrentUsagePeriodEndUtc;
        subscription.NextUsageBillingAtUtc = newSchedule.NextUsageBillingAtUtc;
        subscription.CreditBalanceMinor = newCreditBalanceMinor;

        var outboxEvent = _events.CreatePlanChanged(
            subscription,
            previousPlanCode,
            correlationId);
        var applied = await _subscriptions.TryChangePlanAsync(
            subscription.TenantId,
            subscription.ItemId,
            expectedVersion,
            reservationId,
            newPlan,
            newPrice,
            quantities,
            newSchedule,
            outgoingUsagePeriod,
            newCreditBalanceMinor,
            paymentDetailId,
            outboxEvent,
            cancellationToken);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
        }

        if (_scheduler is not null)
        {
            await _scheduler.ScheduleOutboxPublicationAsync(
                subscription,
                outboxEvent,
                cancellationToken);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription plan changed TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "PreviousPlan={PreviousPlan} NewPlan={NewPlan} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(previousPlanCode),
            PaymentLogValue.Label(newPlan.Code),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<(Plan Plan, Price Price)>> ResolveTargetAsync(
        ChangeSubscriptionPlanRequest request,
        SubscriptionContext context,
        SubscriptionDetail subscription,
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

        if (!string.Equals(price.CurrencyCode, subscription.CurrencyCode, StringComparison.Ordinal))
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.Validation,
                "subscription_plan_change_currency_mismatch",
                "A subscription cannot change to a price in a different currency.",
                correlationId);
        }

        return SubscriptionOperationResult<(Plan, Price)>.Success((plan, price), correlationId);
    }

    private static bool TryBuildSchedule(
        SubscriptionDetail subscription,
        PlanSnapshot targetPlan,
        PriceSnapshot targetPrice,
        DateTime now,
        out SubscriptionPlanSchedule schedule)
    {
        schedule = null!;
        var timeZoneId = subscription.FeeSchedule.TimeZoneId;

        // Moving onto a calendar-aligned price installs that price's own boundaries, exactly as
        // subscribing to it would. The subscriber is not left on their old anniversary until some
        // later renewal notices — the schedule they move onto is the one they were shown.
        var calendarAligned = CalendarBillingAlignment.IsCalendarAligned(targetPrice);

        var feeBuilt = calendarAligned
            ? CalendarBillingAlignment.TryCreateSchedule(
                targetPrice.Interval, now, timeZoneId, out var fee)
            : BillingPeriodCalculator.TryCreateSchedule(
                targetPrice.Interval, targetPrice.IntervalCount, now, timeZoneId, out fee);

        if (!feeBuilt ||
            !BillingPeriodCalculator.TryGetPeriod(fee, now, out var feePeriod) ||
            !BillingPeriodCalculator.TryCreateSchedule(
                targetPlan.UsageInterval, targetPlan.UsageIntervalCount, now, timeZoneId, out var usage) ||
            !BillingPeriodCalculator.TryGetPeriod(usage, now, out var usagePeriod))
        {
            return false;
        }

        var fraction = default(BillingDayFraction);
        var feeStartUtc = feePeriod.StartUtc;
        var feeEndUtc = feePeriod.EndUtc;

        if (calendarAligned)
        {
            if (!CalendarBillingAlignment.TryResolveFirstPeriod(now, timeZoneId, out var first))
            {
                return false;
            }

            // Only a stub replaces the derived period; a change landing on the first opens a
            // whole period at the target's own cadence, which the schedule already derived.
            if (first.IsProrated)
            {
                feeStartUtc = first.StartUtc;
                feeEndUtc = first.EndUtc;
            }

            fraction = BillingDayFraction.Of(first);
        }

        schedule = new SubscriptionPlanSchedule(
            fee,
            feeStartUtc,
            feeEndUtc,
            feeEndUtc,
            usage,
            usagePeriod.StartUtc,
            usagePeriod.EndUtc,
            usagePeriod.EndUtc,
            fraction);
        return true;
    }

    private static PendingUsagePeriod SnapshotOutgoingUsagePeriod(
        SubscriptionDetail subscription,
        string correlationId) => new()
    {
        PeriodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc),
        PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
        PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
        Plan = subscription.Plan,
        Price = subscription.Price,
        CurrencyCode = subscription.CurrencyCode,
        CorrelationId = correlationId
    };

    private static SubscriptionOperationResult<SubscriptionResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionResponse>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
