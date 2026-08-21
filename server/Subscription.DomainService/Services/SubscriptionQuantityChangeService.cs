using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Changes how many units a live subscription has bought, without changing what it is on.
/// </summary>
/// <remarks>
/// The two directions are not symmetrical, because the money is not.
/// <list type="bullet">
/// <item>An <b>increase</b> hands over the units immediately, so it is charged immediately — the
/// prorated difference for what remains of the paid period, taken before the quantity moves. A
/// declined card leaves the subscription exactly as it was.</item>
/// <item>A <b>decrease</b> is not refunded, so it cannot take effect on request: the units are
/// paid for until the period ends and the subscriber keeps them. It is held as a pending change
/// and applied by the renewal.</item>
/// </list>
/// <para>
/// Both directions price through <see cref="QuantityDiscountCalculator"/>, so crossing a volume
/// band is an ordinary consequence of the quantity moving rather than a separate operation.
/// </para>
/// </remarks>
public sealed class SubscriptionQuantityChangeService : ISubscriptionQuantityChangeService
{
    private static readonly SubscriptionStatus[] EligibleStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active
    ];

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IValidator<ChangeQuantityRequest> _validator;
    private readonly ILogger<SubscriptionQuantityChangeService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionQuantityChangeService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IValidator<ChangeQuantityRequest> validator,
        ILogger<SubscriptionQuantityChangeService> logger,
        TimeProvider? time = null)
    {
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public Task<SubscriptionOperationResult<QuantityChangeResponse>> PreviewAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken) =>
        RunAsync(subscriptionId, request, preview: true, correlationId, cancellationToken);

    public Task<SubscriptionOperationResult<QuantityChangeResponse>> ChangeAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken) =>
        RunAsync(subscriptionId, request, preview: false, correlationId, cancellationToken);

    public async Task<SubscriptionOperationResult<QuantityChangeResponse>> CancelPendingAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(subscriptionId, organizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<QuantityChangeResponse>();
        }

        var subscription = loaded.Value!;

        if (subscription.PendingQuantityChange is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_pending_quantity_change_not_found",
                "There is no scheduled quantity change to cancel.",
                correlationId);
        }

        if (!await _subscriptions.TryClearPendingQuantityChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription,
                subscription.QuantityItems,
                subscription.Version + 1,
                preview: false,
                immediate: true,
                effectiveAtUtc: _time.GetUtcNow().UtcDateTime,
                proratedChargeMinor: 0,
                paymentDetailId: null,
                pending: null),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> RunAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        bool preview,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation
            .CheckAsync<ChangeQuantityRequest, QuantityChangeResponse>(
                _validator,
                request,
                "subscription_quantity_invalid",
                "The quantity change request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var loaded = await LoadAsync(
            subscriptionId,
            request.OrganizationId,
            correlationId,
            cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<QuantityChangeResponse>();
        }

        var subscription = loaded.Value!;

        if (!EligibleStatuses.Contains(subscription.Status))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_quantity_change_not_allowed",
                "This subscription cannot change quantity in its current state.",
                correlationId);
        }

        // Checked before anything is calculated, so a stale caller is told to re-read rather than
        // shown a quote derived from a quantity that has already moved.
        if (subscription.Version != request.Version)
        {
            return VersionConflict(correlationId);
        }

        var target = BuildTargetQuantities(subscription, request, out var unknownItemKey);

        if (unknownItemKey is not null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_item_unknown",
                "The plan does not define this quantity item.",
                correlationId);
        }

        if (OutOfBounds(subscription, target, out var offendingKey))
        {
            _logger.LogInformation(
                "Subscription quantity change refused as out of bounds " +
                "SubscriptionHash={SubscriptionHash} Item={Item}",
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(offendingKey));

            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The requested quantity is outside what this plan permits.",
                correlationId);
        }

        var effective = EffectiveQuantities(subscription);

        if (SameQuantities(effective, target))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_unchanged",
                "The requested quantity is already the effective quantity.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var increase = TotalUnits(target) > TotalUnits(effective);

        return increase
            ? await IncreaseAsync(subscription, target, preview, now, correlationId, cancellationToken)
            : await DecreaseAsync(subscription, target, preview, now, correlationId, cancellationToken);
    }

    /// <summary>
    /// An increase: priced for the remainder of the paid period and charged before it applies.
    /// </summary>
    /// <remarks>
    /// The charge comes first deliberately. Granting the units and then billing would leave a
    /// declined card holding seats it never paid for, and taking them back afterwards is a worse
    /// experience than never having been given them.
    /// </remarks>
    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> IncreaseAsync(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        bool preview,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            // The same plan: a quantity change moves the quantity, never the plan. The bands and
            // the combination policy on both sides are the ones the subscriber already holds.
            subscription.Plan,
            subscription.Price,
            target,
            now,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc);

        if (preview)
        {
            return SubscriptionOperationResult<QuantityChangeResponse>.Success(
                Describe(
                    subscription, target, subscription.Version, preview: true, immediate: true,
                    effectiveAtUtc: now, proratedChargeMinor: outcome.ChargeMinor,
                    paymentDetailId: null, pending: null),
                correlationId);
        }

        string? paymentDetailId = null;

        if (outcome.ChargeMinor > 0)
        {
            var account = await _billingAccounts.GetAsync(
                subscription.TenantId,
                subscription.BillingAccountId,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
            {
                return Failure(
                    PaymentFailureKind.Conflict,
                    "subscription_payment_method_missing",
                    "This increase cannot be charged without a saved payment method.",
                    correlationId);
            }

            var charge = await _gateway.ChargeAsync(
                new SubscriptionChargeRequest
                {
                    TenantId = subscription.TenantId,
                    // The merchant's scope, not the subscriber's — see BillingAccount.
                    OrganizationId =
                        account.ProviderOrganizationId ?? subscription.OrganizationId,
                    SubscriberOrganizationId = subscription.OrganizationId,
                    ProviderName = account.ProviderName,
                    StoredPaymentMethodId = account.DefaultPaymentMethodId,
                    ProviderCustomerId = account.ProviderCustomerId,
                    AmountMinor = outcome.ChargeMinor,
                    CurrencyCode = subscription.CurrencyCode,
                    // Keyed on the version being replaced, so a retried request finds the charge
                    // it already raised instead of taking the money twice.
                    OrderId = SubscriptionConstants.QuantityChangeOrderIdFor(
                        subscription.ItemId,
                        subscription.Version),
                    Description = $"{subscription.Plan.DisplayName} quantity change"
                },
                SubscriptionConstants.QuantityChangeKeyFor(
                    subscription.ItemId,
                    subscription.Version),
                correlationId,
                cancellationToken);

            if (!charge.IsSuccess)
            {
                _logger.LogWarning(
                    "Subscription quantity increase declined TenantHash={TenantHash} " +
                    "SubscriptionHash={SubscriptionHash} Reason={Reason}",
                    PaymentLogValue.Hash(subscription.TenantId),
                    PaymentLogValue.Hash(subscription.ItemId),
                    PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

                return charge.ToFailure<QuantityChangeResponse>();
            }

            paymentDetailId = charge.Value;
        }

        var applied = await _subscriptions.TryApplyQuantityChangeAsync(
            subscription.TenantId,
            subscription.ItemId,
            subscription.Version,
            target,
            outcome.NewCreditBalanceMinor,
            paymentDetailId,
            _events.CreateQuantityChanged(subscription, correlationId),
            cancellationToken);

        if (!applied)
        {
            // The money moved and the write did not. Reported as a conflict rather than a
            // success so the caller re-reads; the charge is recoverable by its idempotency key,
            // which is why it is derived from the version rather than random.
            _logger.LogError(
                "A subscription quantity increase was charged but not applied; the charge is " +
                "recoverable by its derived key SubscriptionHash={SubscriptionHash}",
                PaymentLogValue.Hash(subscription.ItemId));

            return VersionConflict(correlationId);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription, target, subscription.Version + 1, preview: false, immediate: true,
                effectiveAtUtc: now, proratedChargeMinor: outcome.ChargeMinor,
                paymentDetailId: paymentDetailId, pending: null),
            correlationId);
    }

    /// <summary>
    /// A decrease: scheduled for the end of the paid period, never refunded.
    /// </summary>
    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> DecreaseAsync(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        bool preview,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var pending = new PendingQuantityChange
        {
            RequestedQuantities = target,
            RequestedAtUtc = now,
            EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            ExpectedVersion = subscription.Version
        };

        if (preview)
        {
            return SubscriptionOperationResult<QuantityChangeResponse>.Success(
                Describe(
                    subscription, target, subscription.Version, preview: true, immediate: false,
                    effectiveAtUtc: pending.EffectiveAtUtc, proratedChargeMinor: 0,
                    paymentDetailId: null, pending: pending),
                correlationId);
        }

        if (!await _subscriptions.TrySetPendingQuantityChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                pending,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription, target, subscription.Version + 1, preview: false, immediate: false,
                effectiveAtUtc: pending.EffectiveAtUtc, proratedChargeMinor: 0,
                paymentDetailId: null, pending: pending),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionDetail>> LoadAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionDetail>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        return subscription is null
            ? SubscriptionOperationResult<SubscriptionDetail>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId)
            : SubscriptionOperationResult<SubscriptionDetail>.Success(subscription, correlationId);
    }

    /// <summary>
    /// The requested quantities merged over what the subscription holds, so a request naming one
    /// item leaves the rest alone.
    /// </summary>
    private static List<SubscriptionQuantityItem> BuildTargetQuantities(
        SubscriptionDetail subscription,
        ChangeQuantityRequest request,
        out string? unknownItemKey)
    {
        unknownItemKey = null;

        var target = subscription.QuantityItems
            .Select(item => new SubscriptionQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                Quantity = item.Quantity,
                UnitAmountMinor = item.UnitAmountMinor
            })
            .ToList();

        foreach (var requested in request.Quantities)
        {
            var held = target.Find(item =>
                string.Equals(item.ItemKey, requested.ItemKey, StringComparison.Ordinal));

            if (held is null)
            {
                unknownItemKey = requested.ItemKey;
                return target;
            }

            held.Quantity = requested.Quantity;
        }

        return target;
    }

    /// <summary>
    /// The quantities in force for pricing purposes — a pending decrease has not happened yet, so
    /// what is on the subscription is still what the subscriber holds.
    /// </summary>
    private static List<SubscriptionQuantityItem> EffectiveQuantities(
        SubscriptionDetail subscription) => subscription.QuantityItems;

    /// <summary>Bounds come from the snapshot, not the catalogue, like every other plan term.</summary>
    private static bool OutOfBounds(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        out string offendingKey)
    {
        foreach (var item in target)
        {
            var defined = subscription.Plan.QuantityItems.Find(candidate =>
                string.Equals(candidate.ItemKey, item.ItemKey, StringComparison.Ordinal));

            if (defined is null ||
                item.Quantity < defined.MinQuantity ||
                (defined.MaxQuantity is { } maximum && item.Quantity > maximum))
            {
                offendingKey = item.ItemKey;
                return true;
            }
        }

        offendingKey = string.Empty;
        return false;
    }

    private static bool SameQuantities(
        IReadOnlyList<SubscriptionQuantityItem> left,
        IReadOnlyList<SubscriptionQuantityItem> right) =>
        left.Count == right.Count &&
        left.All(item => right.Any(other =>
            string.Equals(other.ItemKey, item.ItemKey, StringComparison.Ordinal) &&
            other.Quantity == item.Quantity));

    private static long TotalUnits(IReadOnlyList<SubscriptionQuantityItem> items) =>
        items.Sum(item => item.Quantity);

    private QuantityChangeResponse Describe(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        int version,
        bool preview,
        bool immediate,
        DateTime effectiveAtUtc,
        long proratedChargeMinor,
        string? paymentDetailId,
        PendingQuantityChange? pending)
    {
        var current = QuantityDiscountCalculator.ResolveFrom(
            subscription.Plan,
            subscription.Price,
            subscription.QuantityItems);

        var next = QuantityDiscountCalculator.ResolveFrom(
            subscription.Plan,
            subscription.Price,
            target);

        // What the next renewal charges, priced through the same path the renewal itself uses so
        // the figure shown cannot drift from the figure taken.
        var atTarget = CloneAtQuantities(subscription, target);
        var renewal = SubscriptionAmountCalculator.PeriodAmountMinor(
            atTarget,
            _time.GetUtcNow().UtcDateTime);

        return new QuantityChangeResponse
        {
            SubscriptionId = subscription.ItemId,
            Version = version,
            Preview = preview,
            Timing = immediate ? "Immediate" : "NextPeriod",
            EffectiveAtUtc = effectiveAtUtc,
            CurrencyCode = subscription.CurrencyCode,
            Quantities = target.Select(ToItemResponse).ToList(),
            CurrentTier = ToTierResponse(current.Tier),
            TargetTier = ToTierResponse(next.Tier),
            ProratedChargeMinor = proratedChargeMinor,
            NextRenewalAmountMinor = renewal.AmountMinor,
            ChargePaymentDetailId = paymentDetailId,
            PendingQuantityChange = pending is null
                ? null
                : new PendingQuantityChangeResponse
                {
                    Quantities = pending.RequestedQuantities.Select(ToItemResponse).ToList(),
                    RequestedAtUtc = pending.RequestedAtUtc,
                    EffectiveAtUtc = pending.EffectiveAtUtc
                }
        };
    }

    /// <summary>
    /// The subscription as it would stand at the target quantities, for pricing only. Never
    /// persisted — a shallow copy is enough because only the quantities differ.
    /// </summary>
    private static SubscriptionDetail CloneAtQuantities(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target) => new()
    {
        ItemId = subscription.ItemId,
        TenantId = subscription.TenantId,
        OrganizationId = subscription.OrganizationId,
        Status = subscription.Status,
        Plan = subscription.Plan,
        Price = subscription.Price,
        QuantityItems = target,
        CurrencyCode = subscription.CurrencyCode,
        Discount = subscription.Discount,
        DiscountPeriodsApplied = subscription.DiscountPeriodsApplied,
        // Deliberately not the credit balance: a renewal quote is what the period costs, and
        // banked credit is settled against it separately.
        CreditBalanceMinor = 0
    };

    private static QuantityChangeItemResponse ToItemResponse(SubscriptionQuantityItem item) => new()
    {
        ItemKey = item.ItemKey,
        UnitLabel = item.UnitLabel,
        Quantity = item.Quantity
    };

    private static QuantityDiscountTierResponse? ToTierResponse(QuantityDiscountTier? tier) =>
        tier is null
            ? null
            : new QuantityDiscountTierResponse
            {
                MinimumQuantity = tier.MinimumQuantity,
                MaximumQuantity = tier.MaximumQuantity,
                DiscountBasisPoints = tier.DiscountBasisPoints
            };

    private static SubscriptionOperationResult<QuantityChangeResponse> VersionConflict(
        string correlationId) =>
        Failure(
            PaymentFailureKind.Conflict,
            "subscription_version_conflict",
            "The subscription changed while this request was in flight. Re-read and try again.",
            correlationId);

    private static SubscriptionOperationResult<QuantityChangeResponse> Failure(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId) =>
        SubscriptionOperationResult<QuantityChangeResponse>.Failure(kind, code, message, correlationId);
}
