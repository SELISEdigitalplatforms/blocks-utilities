using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
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

    /// <summary>
    /// Where the next renewal is announced, when the queue is in use.
    /// </summary>
    /// <remarks>
    /// Optional, and never load-bearing. A renewal that has already charged and written must not be
    /// reported as failed because a scheduling write did not land — the repair sweep exists for
    /// exactly that, and this is the one ordering that keeps money and bookkeeping from trading
    /// places.
    /// </remarks>
    private readonly ISubscriptionWorkScheduler? _scheduler;
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
        ISubscriptionAuditTrail? audit = null,
        ISubscriptionWorkScheduler? scheduler = null)
    {
        _scheduler = scheduler;
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

        // Which instant the period being charged belongs to. Normally now — but a card-free trial
        // converting is charged for the period its *trial ended* in, however late this sweep runs.
        // Anchoring on the clock instead would skip the days between the trial's end and today,
        // and those are days the subscriber was entitled to and nobody billed.
        var converting = TryResolveTrialConversion(subscription, now, out var trialEndUtc, out var stub);
        var periodAnchorUtc = converting ? trialEndUtc : now;

        if (!BillingPeriodCalculator.TryGetPeriod(
                subscription.FeeSchedule,
                periodAnchorUtc,
                out var period))
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

        // A card-free trial that ends mid-month buys the rest of that month, not all of it. This
        // is the only renewal that can be charging for a partial period: every later one runs on a
        // boundary, where the period it opens is whole by construction.
        //
        // The period *key* is deliberately left as the calendar month's, which is what the order id
        // and idempotency key above were built from. A late sweep charging August must key on
        // August, so that when it then advances to 1 September the next pass raises a genuinely
        // different charge rather than colliding with this one.
        var fraction = converting ? BillingDayFraction.Of(stub) : default;

        if (converting)
        {
            period = period with { StartUtc = stub.StartUtc, EndUtc = stub.EndUtc };
        }

        // Priced at the instant the period being charged *began*, not the instant this sweep runs.
        // Whether a promotion is still live depends on the clock, so pricing a conversion from
        // "now" would charge a subscriber who was mid-promotion when their trial ended the
        // undiscounted amount purely because a worker was held up — and would make the same
        // contractual period cost two different figures depending on sweep latency. Only the
        // conversion moves it; an ordinary renewal begins at the boundary it is running on.
        var pricingInstantUtc = converting ? trialEndUtc : now;

        // The year a calendar-aligned yearly subscription bought at signup, now due to open. Its
        // amount was frozen with the quote and is not re-derived here — the boundary is a month
        // after the checkout that priced it.
        var openingAnnualPeriod = DueAnnualPeriod(subscription, period);

        var charge = openingAnnualPeriod is { } annual
            ? new PeriodCharge(
                // Prepaid means the money came in with the opening charge, so this boundary moves
                // the subscription into the year and takes nothing. Charging again here would be
                // the same year billed twice.
                annual.IsPrepaid ? 0 : annual.AmountMinor,
                annual.DiscountApplied,
                NetAmountMinor: annual.NetAmountMinor,
                TaxAmountMinor: annual.TaxAmountMinor,
                GrossAmountMinor: annual.GrossAmountMinor,
                BuiltInDiscountMinor: annual.BuiltInDiscountMinor,
                PromotionalDiscountMinor: annual.PromotionalDiscountMinor)
            : SubscriptionAmountCalculator.PeriodAmountMinor(
                subscription,
                pricingInstantUtc,
                fraction);

        if (openingAnnualPeriod is not null)
        {
            // The period is the one that was quoted, not one derived now. They agree in the
            // ordinary case; using the stored pair means they cannot drift if anything about the
            // schedule is later corrected.
            period = period with
            {
                StartUtc = openingAnnualPeriod.StartUtc,
                EndUtc = openingAnnualPeriod.EndUtc
            };
        }

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
                    // The split as it was calculated, so a renewal invoice can show a subtotal and
                    // a tax line that add up to the charge above. Credit is not part of it: it pays
                    // the bill rather than changing what the bill was for, so a credited renewal
                    // sends a net and tax that describe more than AmountMinor — the gateway falls
                    // back to a single line when they disagree.
                    NetAmountMinor = charge.NetAmountMinor,
                    TaxAmountMinor = charge.TaxAmountMinor,
                    TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
                    TaxMode = subscription.Price.TaxMode,
                    CreditConsumedMinor = charge.CreditConsumedMinor,
                    // What came off before tax, from the same calculation the amount came from, so
                    // the payment record can explain its own total later.
                    GrossAmountMinor = charge.GrossAmountMinor,
                    BuiltInDiscountMinor = charge.BuiltInDiscountMinor,
                    PromotionalDiscountMinor = charge.PromotionalDiscountMinor,
                    AutomaticDiscountBasisPoints =
                        SubscriptionDiscountPresentation.RateOf(subscription.Price),
                    QuantityDiscountBasisPoints = QuantityDiscountCalculator.ResolveFrom(
                        subscription.Plan,
                        subscription.Price,
                        subscription.QuantityItems).Tier?.DiscountBasisPoints,
                    DiscountCombination =
                        SubscriptionDiscountPresentation.Describe(subscription.Price),
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
                charge,
                converting ? fraction : null,
                outcome.Value,
                attemptNumber,
                pendingQuantities,
                openingAnnualPeriod is not null,
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

    /// <summary>
    /// Whether this renewal is a calendar-aligned card-free trial converting to paid, and if so
    /// which period it should charge for.
    /// </summary>
    /// <remarks>
    /// Anchored on the trial's own end date and nothing else. A sweep that runs the next morning,
    /// or a fortnight later after an outage, must still charge for the period the subscriber was
    /// entitled to from — pricing from "now" would shorten it by however late the sweep ran, and
    /// once the clock has crossed a month boundary it would skip those days entirely.
    /// <para>
    /// The caller charges that period, advances to its end, and leaves the subscription due again
    /// immediately. A conversion discovered a fortnight late therefore bills the stub it owes and
    /// then the whole months since, one boundary at a time, each under its own period key.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> keyed on <see cref="SubscriptionStatus.Trialing"/>. The first
    /// attempt at a conversion can decline, which moves the subscription to
    /// <see cref="SubscriptionStatus.PastDue"/> — and a dunning retry that no longer recognised
    /// the conversion would abandon the unpaid stub and bill whatever month the clock had reached
    /// by then. What actually ends a conversion is its first paid period being recorded, so that
    /// is what this asks about.
    /// </para>
    /// </remarks>
    private static bool TryResolveTrialConversion(
        SubscriptionDetail subscription,
        DateTime nowUtc,
        out DateTime trialEndUtc,
        out CalendarFirstPeriod stub)
    {
        trialEndUtc = default;
        stub = default;

        if (subscription.InitialChargeAmountMinor is not null ||
            !CalendarBillingAlignment.IsCalendarAligned(subscription.Price) ||
            subscription.Trial is not { RequiresPaymentMethod: false, EndsAtUtc: var endsAtUtc } ||
            endsAtUtc > nowUtc)
        {
            return false;
        }

        trialEndUtc = endsAtUtc;

        return CalendarBillingAlignment.TryResolveFirstPeriod(
            endsAtUtc,
            subscription.FeeSchedule.TimeZoneId,
            out stub);
    }

    /// <param name="firstPaidFraction">
    /// The day fraction this charge covered, when it was a card-free trial's first paid period and
    /// therefore the moment those tracing fields become knowable. Null on every ordinary renewal,
    /// which must never overwrite what the original checkout froze.
    /// </param>
    /// <param name="openedAnnualPeriod">
    /// Whether this transition is the one moving a calendar-aligned yearly subscription out of its
    /// opening stub and into the year it bought, so the pending record is discarded with it.
    /// </param>
    private async Task ApplySuccessAsync(
        SubscriptionDetail subscription,
        BillingPeriod period,
        PeriodCharge charge,
        BillingDayFraction? firstPaidFraction,
        string? paymentDetailId,
        int attemptNumber,
        List<SubscriptionQuantityItem>? appliedQuantities,
        bool openedAnnualPeriod,
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
                // Opening the year and forgetting that it was pending must be one write, or the
                // next sweep finds it again and charges for the same year twice.
                ClearPendingAnnualPeriod = openedAnnualPeriod,
                DiscountPeriodsApplied = subscription.DiscountPeriodsApplied +
                    (charge.DiscountApplied ? 1 : 0),
                // Written in the same transition that opens the period they describe, so a trial
                // that converted can always account for what its first paid charge was.
                InitialChargeAmountMinor = firstPaidFraction is null ? null : charge.AmountMinor,
                InitialChargeProrated = firstPaidFraction?.IsPartial,
                InitialChargeDiscountApplied =
                    firstPaidFraction is null ? null : charge.DiscountApplied,
                ProrationDays = firstPaidFraction is { IsPartial: true } paid
                    ? paid.CoveredDays
                    : null,
                ProrationTotalDays = firstPaidFraction is { IsPartial: true } paidTotal
                    ? paidTotal.TotalDays
                    : null,
                CreditBalanceMinor = subscription.CreditBalanceMinor - charge.CreditConsumedMinor,
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

        await ScheduleNextRenewalAsync(subscription, period, cancellationToken);
        await AuditAsync(subscription, "StateApplied", "Succeeded", null, null,
            paymentDetailId, attemptNumber, cancellationToken);
    }

    /// <summary>
    /// Announces the period that has just become due, so nothing has to go looking for it.
    /// </summary>
    /// <remarks>
    /// Keyed on the new period, which makes it idempotent for free: the sweep scheduling the same
    /// period, or a second worker renewing concurrently, lands on the one occurrence.
    /// <para>
    /// Failures are swallowed deliberately. The money has moved and the renewal is recorded; a
    /// scheduling write that fails costs a later start, not a lost renewal, and the sweep finds it.
    /// Throwing here would turn a bookkeeping problem into a renewal that looks unfinished.
    /// </para>
    /// </remarks>
    private async Task ScheduleNextRenewalAsync(
        SubscriptionDetail subscription,
        BillingPeriod period,
        CancellationToken cancellationToken)
    {
        if (_scheduler is null)
        {
            return;
        }

        try
        {
            await _scheduler.ScheduleAsync(
                SubscriptionWorkType.Renewal,
                subscription.TenantId,
                $"renewal:{period.Key}",
                period.EndUtc,
                subscription.CorrelationId,
                subscription.ItemId,
                subscription.OrganizationId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A renewal succeeded but its next occurrence could not be scheduled; the repair " +
                "sweep will find it PeriodKey={PeriodKey}",
                PaymentLogValue.Label(period.Key));
        }
    }

    /// <summary>
    /// The pending year this renewal is opening, or null when it is an ordinary renewal.
    /// </summary>
    /// <remarks>
    /// Due once the boundary it was quoted for has arrived. A subscription still inside its stub
    /// has nothing to open — its stub is the period it is being renewed out of, and that renewal
    /// is this one.
    /// </remarks>
    private static PendingAnnualPeriod? DueAnnualPeriod(
        SubscriptionDetail subscription,
        BillingPeriod period) =>
        subscription.PendingAnnualPeriod is { } pending &&
        pending.StartUtc <= period.StartUtc
            ? pending
            : null;

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
