using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Charges a subscription's renewal, and drives dunning when it declines.
/// </summary>
/// <remarks>
/// One method handles a normal renewal, a dunning retry, and a trial converting to paid — all
/// three are "charge the stored card for the period that is due," and none needs to know which
/// of the three it is. A trial with no stored card behaves the same as a dunning cycle with no
/// card: there is nothing to retry, so it goes straight to <see cref="SubscriptionStatus.Unpaid"/>.
/// </remarks>
public sealed class SubscriptionRenewalService : ISubscriptionRenewalService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionRenewalService> _logger;
    private readonly TimeProvider _time;
    private readonly ISubscriptionAuditTrail? _audit;

    public SubscriptionRenewalService(
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionRenewalService> logger,
        TimeProvider? time = null,
        ISubscriptionAuditTrail? audit = null)
    {
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _audit = audit;
    }

    public async Task RenewAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantHash"] = PaymentLogValue.Hash(subscription.TenantId),
            ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
        });

        var now = _time.GetUtcNow().UtcDateTime;
        await AuditAsync(subscription, "Started", "InProgress", null, null, null,
            subscription.DunningAttemptCount + 1, cancellationToken);

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            // Retrying without a card to charge is pointless — including a trial that never
            // took one, which reaches this exact path at its end.
            await MoveToUnpaidAsync(subscription, "no_payment_method", cancellationToken);
            await AuditAsync(subscription, "PaymentMethodChecked", "Failed",
                "no_payment_method", null, null, subscription.DunningAttemptCount + 1,
                cancellationToken);

            return;
        }

        if (!BillingPeriodCalculator.TryGetPeriod(subscription.FeeSchedule, now, out var period))
        {
            // A schedule that resolved at creation and stopped resolving is a configuration
            // problem, not a billing outcome — leave the subscription as it is and let the next
            // sweep try again rather than guessing at a period to write.
            _logger.LogError(
                "Subscription renewal could not resolve a billing period; the schedule's time " +
                "zone may no longer be valid");
            await AuditAsync(subscription, "PeriodResolved", "Failed",
                "billing_period_unresolvable", null, null, null, cancellationToken);

            return;
        }

        var attemptNumber = subscription.DunningAttemptCount + 1;
        var orderId = SubscriptionConstants.RenewalOrderIdFor(subscription.ItemId, period.Key);
        var idempotencyKey = SubscriptionConstants.RenewalKeyFor(
            subscription.ItemId,
            period.Key,
            attemptNumber);

        // A decrease scheduled for the end of the period now closing takes effect from here, so
        // this renewal is the first one priced at the new quantity — and the invoice it produces
        // must say the same. Applied to the in-memory subscription before pricing, and written in
        // the same transition that advances the period.
        var pendingQuantities = DueQuantityChange(subscription);

        if (pendingQuantities is not null)
        {
            subscription.QuantityItems = pendingQuantities;
        }

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, now);

        var outcome = charge.AmountMinor <= 0
            ? SubscriptionOperationResult<string>.Success(string.Empty, subscription.CorrelationId)
            : await _gateway.ChargeAsync(
                new SubscriptionChargeRequest
                {
                    TenantId = subscription.TenantId,
                    // The merchant's scope, not the subscriber's: the tenant configures one
                    // provider and every organization is charged through it. Falls back for
                    // accounts predating the field, which used the subscriber's.
                    OrganizationId =
                        account.ProviderOrganizationId ?? subscription.OrganizationId,
                    SubscriberOrganizationId = subscription.OrganizationId,
                    ProviderName = account.ProviderName,
                    StoredPaymentMethodId = account.DefaultPaymentMethodId,
                    ProviderCustomerId = account.ProviderCustomerId,
                    AmountMinor = charge.AmountMinor,
                    CurrencyCode = subscription.CurrencyCode,
                    OrderId = orderId,
                    Description = $"{subscription.Plan.DisplayName} renewal"
                },
                idempotencyKey,
                subscription.CorrelationId,
                cancellationToken);

        await AuditAsync(subscription, "ChargeCompleted",
            outcome.IsSuccess ? "Succeeded" : "Failed", outcome.ErrorCode,
            charge.AmountMinor, outcome.Value, attemptNumber, cancellationToken);

        if (outcome.IsSuccess)
        {
            await ApplySuccessAsync(
                subscription,
                period,
                charge.DiscountApplied,
                charge.CreditConsumedMinor,
                outcome.Value,
                attemptNumber,
                pendingQuantities,
                cancellationToken);

            return;
        }

        _logger.LogWarning(
            "Subscription renewal declined AttemptNumber={AttemptNumber} Reason={Reason}",
            attemptNumber,
            PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"));

        await ApplyFailureAsync(subscription, period.Key, attemptNumber, now, cancellationToken);
    }

    private Task AuditAsync(
        SubscriptionDetail subscription,
        string stage,
        string outcome,
        string? errorCode,
        long? amountMinor,
        string? paymentDetailId,
        int? attempt,
        CancellationToken cancellationToken) =>
        _audit is null
            ? Task.CompletedTask
            : _audit.RecordAsync(new SubscriptionAuditEvent
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                OperationId = $"renewal:{subscription.ItemId}:{subscription.CurrentPeriodEndUtc:O}",
                CorrelationId = subscription.CorrelationId,
                Operation = "Renewal",
                Stage = stage,
                Outcome = outcome,
                Source = "Worker",
                PaymentDetailId = paymentDetailId,
                AmountMinor = amountMinor,
                CurrencyCode = subscription.CurrencyCode,
                FromStatus = subscription.Status.ToString(),
                ErrorCode = errorCode,
                Attempt = attempt
            }, cancellationToken);

    /// <summary>
    /// The quantities a scheduled decrease puts in force, or null when nothing is due.
    /// </summary>
    /// <remarks>
    /// Due once its effective instant has passed. Read here rather than on a timer because the
    /// renewal is already the thing that runs at a period boundary, and giving a decrease its own
    /// sweep would mean two clocks that could disagree about which period a quantity belonged to.
    /// </remarks>
    private static List<SubscriptionQuantityItem>? DueQuantityChange(SubscriptionDetail subscription) =>
        subscription.PendingQuantityChange is { } pending &&
        pending.EffectiveAtUtc <= subscription.CurrentPeriodEndUtc
            ? pending.RequestedQuantities
            : null;

    private async Task ApplySuccessAsync(
        SubscriptionDetail subscription,
        BillingPeriod period,
        bool discountApplied,
        long creditConsumedMinor,
        string? paymentDetailId,
        int attemptNumber,
        List<SubscriptionQuantityItem>? appliedQuantities,
        CancellationToken cancellationToken)
    {
        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(subscription.Status, SubscriptionStatus.Active)
            {
                ActivatedAtUtc = subscription.ActivatedAtUtc ?? _time.GetUtcNow().UtcDateTime,
                // A quantity increase taken between reading this subscription and writing here
                // would be granted after the period it was prorated against had closed, on top of a
                // period billed at the smaller quantity. Refused rather than reconciled: the next
                // pass renews once the reservation is resolved.
                RequireNoSettlementReservation = true,
                CurrentPeriodStartUtc = period.StartUtc,
                CurrentPeriodEndUtc = period.EndUtc,
                NextFeeBillingAtUtc = period.EndUtc,
                ClearPastDueSinceAt = true,
                DunningAttemptCount = 0,
                // Both in the one transition: applying the quantity and forgetting the schedule
                // must not come apart, or the next renewal applies it again.
                QuantityItems = appliedQuantities,
                ClearPendingQuantityChange = appliedQuantities is not null,
                DiscountPeriodsApplied = subscription.DiscountPeriodsApplied +
                    (discountApplied ? 1 : 0),
                CreditBalanceMinor = subscription.CreditBalanceMinor - creditConsumedMinor,
                LastRenewalPaymentDetailId = string.IsNullOrEmpty(paymentDetailId)
                    ? null
                    : paymentDetailId,
                Event = _events.CreateRenewalOutcome(
                    subscription,
                    SubscriptionConstants.SubscriptionRenewed,
                    period.Key,
                    attemptNumber,
                    subscription.CorrelationId)
            },
            cancellationToken);

        if (!applied)
        {
            // Either another worker already settled this renewal — its outcome stands — or a
            // settlement reservation was taken between reading this subscription and writing here.
            // Both are safe to walk away from: the charge is keyed on the period and the attempt
            // number, neither of which this failure moves, so the next pass raises no second charge
            // and finds the one already made.
            _logger.LogInformation(
                "A renewal was charged but not applied and will be retried " +
                "AttemptNumber={AttemptNumber} PeriodKey={PeriodKey} " +
                "ReservationHeld={ReservationHeld}",
                attemptNumber,
                PaymentLogValue.Label(period.Key),
                subscription.SettlementReservation is not null);
            await AuditAsync(subscription, "StateApplied", "Deferred",
                "renewal_state_conflict", null, paymentDetailId, attemptNumber,
                cancellationToken);

            return;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription renewed AttemptNumber={AttemptNumber} PeriodKey={PeriodKey}",
            attemptNumber,
            PaymentLogValue.Label(period.Key));
        await AuditAsync(subscription, "StateApplied", "Succeeded", null, null,
            paymentDetailId, attemptNumber, cancellationToken);
    }

    private async Task ApplyFailureAsync(
        SubscriptionDetail subscription,
        string periodKey,
        int attemptNumber,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.CurrentValue.DunningMaxAttempts);

        if (subscription.Status != SubscriptionStatus.PastDue)
        {
            await ApplyTransitionAsync(
                subscription,
                subscription.Status,
                SubscriptionStatus.PastDue,
                periodKey,
                attemptNumber,
                new SubscriptionTransition(subscription.Status, SubscriptionStatus.PastDue)
                {
                    PastDueSinceUtc = now,
                    DunningAttemptCount = attemptNumber,
                    NextFeeBillingAtUtc = NextDunningAttemptAt(now),
                    Event = _events.CreateRenewalOutcome(
                        subscription,
                        SubscriptionConstants.SubscriptionPastDue,
                        periodKey,
                        attemptNumber,
                        subscription.CorrelationId)
                },
                cancellationToken);

            return;
        }

        if (attemptNumber < maxAttempts)
        {
            await ApplyTransitionAsync(
                subscription,
                SubscriptionStatus.PastDue,
                SubscriptionStatus.PastDue,
                periodKey,
                attemptNumber,
                new SubscriptionTransition(SubscriptionStatus.PastDue, SubscriptionStatus.PastDue)
                {
                    DunningAttemptCount = attemptNumber,
                    NextFeeBillingAtUtc = NextDunningAttemptAt(now),
                    Event = _events.CreateRenewalOutcome(
                        subscription,
                        SubscriptionConstants.SubscriptionRenewalFailed,
                        periodKey,
                        attemptNumber,
                        subscription.CorrelationId)
                },
                cancellationToken);

            return;
        }

        await MoveToUnpaidAsync(subscription, "dunning_exhausted", cancellationToken);
    }

    private async Task MoveToUnpaidAsync(
        SubscriptionDetail subscription,
        string reason,
        CancellationToken cancellationToken)
    {
        if (subscription.Status == SubscriptionStatus.Unpaid)
        {
            return;
        }

        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(subscription.Status, SubscriptionStatus.Unpaid)
            {
                ClearPastDueSinceAt = true,
                ClearNextFeeBillingAt = true,
                DunningAttemptCount = 0,
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionUnpaid,
                    subscription.CorrelationId)
            },
            cancellationToken);

        if (!applied)
        {
            return;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription moved to unpaid Reason={Reason}",
            PaymentLogValue.Label(reason));
    }

    private async Task ApplyTransitionAsync(
        SubscriptionDetail subscription,
        SubscriptionStatus expected,
        SubscriptionStatus target,
        string periodKey,
        int attemptNumber,
        SubscriptionTransition transition,
        CancellationToken cancellationToken)
    {
        var applied = await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            transition,
            cancellationToken);

        if (!applied)
        {
            return;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription renewal outcome recorded FromStatus={FromStatus} ToStatus={ToStatus} " +
            "AttemptNumber={AttemptNumber} PeriodKey={PeriodKey}",
            PaymentLogValue.Label(expected.ToString()),
            PaymentLogValue.Label(target.ToString()),
            attemptNumber,
            PaymentLogValue.Label(periodKey));
    }

    private DateTime NextDunningAttemptAt(DateTime now) =>
        now.AddHours(Math.Max(1, _options.CurrentValue.DunningRetryIntervalHours));
}
