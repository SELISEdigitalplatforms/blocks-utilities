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
        ISubscriptionWorkScheduler? scheduler = null,
        ISubscriptionFinancialDocumentAnnouncer? documents = null,
        ISubscriptionUsageRepository? usage = null,
        IMeterAllowanceResolver? allowances = null)
    {
        _usage = usage;
        _allowances = allowances;
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
        _documents = documents;
    }

    /// <summary>Optional for the reason the scheduler beside it is: a renewal must not need one.</summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

    /// <summary>
    /// Reads the outgoing usage window's carried-in allowances when a scheduled plan change moves
    /// the usage schedule at this boundary — the same snapshot a plan change applied immediately
    /// takes, for the same reason: once the new schedule is installed, a carry-forward meter's
    /// allowance for the window just closed can no longer be resolved. Both optional, and a null
    /// pair simply falls back to live resolution at rating time.
    /// </summary>
    private readonly ISubscriptionUsageRepository? _usage;
    private readonly IMeterAllowanceResolver? _allowances;

    /// <inheritdoc />
    public Task RecoverAsync(SubscriptionDetail subscription, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Status != SubscriptionStatus.Unpaid)
        {
            // Called from exactly one place: a card confirmed against a subscription that is
            // already Unpaid. Anything else reaching here is a caller mistake, and this exists
            // specifically because charging a subscription through the wrong entry point is the
            // one failure mode that costs real money -- so it is refused rather than guessed at.
            _logger.LogWarning(
                "RecoverAsync called for a subscription that is not Unpaid TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Status={Status}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(subscription.Status.ToString()));

            return Task.CompletedTask;
        }

        // Everything after this point is the same charge, the same idempotency key derivation, and
        // the same compare-and-set transition an ordinary renewal uses -- reused rather than
        // duplicated so this money follows one set of rules, not two. What makes it safe to reuse
        // for a subscription that lost access is the pair of fixes above it: the period and price
        // are anchored on the trial's own end regardless of how long the subscription sat Unpaid,
        // and a decline here leaves Unpaid alone instead of granting access through PastDue.
        return RenewAsync(subscription, cancellationToken);
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

        // Which instant the period being charged belongs to. Normally now — but a trial converting
        // for the first time is charged for the period its *trial ended* in, however late this
        // runs. Anchoring on the clock instead would skip the days between the trial's end and
        // today, and those are days the subscriber was entitled to and nobody billed.
        var converting = TryResolveTrialConversion(subscription, now, out var trialEndUtc, out var stub);

        // The calendar case above covers a price with a variable-length opening stub. A price
        // billed on its own anniversary has no stub — its first paid period is simply the whole
        // period the schedule's own anchor already sits at, which is the trial's end (the schedule
        // was built that way). That makes "now" a safe stand-in only while this runs promptly, and
        // it does not run promptly for a subscription that sat Unpaid until somebody came back
        // with a card: TryGetPeriod(schedule, now) would then resolve whatever period "now" falls
        // in, silently skipping the one actually owed, and price it at whatever is live today
        // rather than what was quoted when the trial ended.
        var firstConversionUtc = subscription.Trial is { EndsAtUtc: var trialEndsAtUtc } &&
            subscription.InitialChargeAmountMinor is null &&
            trialEndsAtUtc <= now
                ? trialEndsAtUtc
                : (DateTime?)null;

        var periodAnchorUtc = converting ? trialEndUtc : firstConversionUtc ?? now;

        // Selected before the period is resolved, because it decides which schedule resolves it.
        // A monthly-to-annual change resolved against the outgoing monthly schedule would charge
        // the annual price and then persist a period ending one month later — leaving the
        // subscription due again next month for a year it has just paid for.
        //
        // Both can never be pending at once — the repository refuses to hold a plan change over a
        // quantity change and vice versa — so there is no question here of which wins.
        var pendingPlan = DuePlanChange(subscription);

        // The rhythm this renewal actually opens a period on: the target's where a change is due
        // at this boundary, the subscription's own otherwise.
        var effectiveFeeSchedule = pendingPlan?.FeeSchedule ?? subscription.FeeSchedule;

        if (!BillingPeriodCalculator.TryGetPeriod(
                effectiveFeeSchedule,
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

        // Frozen before the schedule below is swapped, and only when the swap actually re-anchors
        // metering. A carry-forward meter's carried-in allowance for the window now closing cannot
        // be resolved once UsageSchedule names a different rhythm — the same defect an immediate
        // plan change already guards against by snapshotting before it re-anchors.
        var outgoingUsagePeriod = pendingPlan is not null &&
            MovesUsageSchedule(subscription.UsageSchedule, pendingPlan.UsageSchedule)
                ? await SnapshotOutgoingUsagePeriodAsync(subscription, cancellationToken)
                : null;

        if (pendingPlan is not null)
        {
            subscription.Plan = pendingPlan.Plan;
            subscription.Price = pendingPlan.Price;
            subscription.QuantityItems = pendingPlan.QuantityItems;
            subscription.FeeSchedule = pendingPlan.FeeSchedule;
            subscription.UsageSchedule = pendingPlan.UsageSchedule;
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

        // A trial that ends on the local first has no stub to buy: the period it converts into is a
        // whole one at the price's own cadence, which the schedule already derived. Truncating it
        // to the stub's month would charge a year's money for a month and leave a second year
        // pending behind it.
        var convertingToStub = converting && stub.IsProrated;

        if (convertingToStub)
        {
            period = period with { StartUtc = stub.StartUtc, EndUtc = stub.EndUtc };
        }

        // Priced at the instant the period being charged *began*, not the instant this sweep runs.
        // Whether a promotion is still live depends on the clock, so pricing a conversion from
        // "now" would charge a subscriber who was mid-promotion when their trial ended the
        // undiscounted amount purely because a worker was held up, or because they were Unpaid for
        // a week before adding a card — and would make the same contractual period cost two
        // different figures depending on when this happened to run. Only a first conversion moves
        // it; an ordinary renewal begins at the boundary it is running on.
        var pricingInstantUtc = converting ? trialEndUtc : firstConversionUtc ?? now;

        // The year a calendar-aligned yearly subscription bought at signup, now due to open. Its
        // amount was frozen with the quote and is not re-derived here — the boundary is a month
        // after the checkout that priced it.
        var openingAnnualPeriod = DueAnnualPeriod(subscription, period);

        // A converting trial's year, priced here for the same reason its stub is: which month the
        // trial ended in decides both, and neither was knowable at signup.
        // Only a stub is followed by a year that has to be remembered. A conversion that opened a
        // whole year is already inside it, and holding another would bill the same subscriber for
        // two.
        var convertingAnnual = convertingToStub
            ? SubscriptionCreationService.BuildPendingAnnualPeriod(
                subscription,
                period.EndUtc,
                pricingInstantUtc)
            : null;

        var charge = openingAnnualPeriod is { } annual
            ? new PeriodCharge(
                // Settled means the money came in with the opening charge, so this boundary moves
                // the subscription into the year and takes nothing. Charging again would bill the
                // same year twice.
                //
                // Read from what the activation recorded, never from the price's configuration: a
                // year that was meant to be collected at checkout but never was is a year still
                // owed, and only the payment can say which it is.
                annual.IsPrepaid ? 0 : annual.AmountMinor,
                // A limited promotion is spent by the charge that reduced money, and a prepaid year
                // reduced it once already — at the checkout or the trial conversion that collected
                // it. Counting it again here, where nothing is taken, would expire a three-month
                // promotion after two bills.
                //
                // A year still owed is the opposite case: this boundary is the charge that spends
                // it, so it is reported as applied exactly once, here.
                DiscountApplied: !annual.IsPrepaid && annual.DiscountApplied,
                NetAmountMinor: annual.NetAmountMinor,
                TaxAmountMinor: annual.TaxAmountMinor,
                GrossAmountMinor: annual.GrossAmountMinor,
                BuiltInDiscountMinor: annual.BuiltInDiscountMinor,
                PromotionalDiscountMinor: annual.PromotionalDiscountMinor)
            : SubscriptionAmountCalculator.PeriodAmountMinor(
                subscription,
                pricingInstantUtc,
                fraction,
                // A promotional code belongs to the year, so a yearly stub is priced without one.
                includePromotionalDiscount: convertingAnnual is null);

        // "Collect the year with the first payment" — and for a card-free trial this conversion is
        // the first payment there has ever been. Taken together with the stub in one charge, and
        // the year is settled from the moment it succeeds.
        if (convertingAnnual is { CollectedWithCheckout: true })
        {
            charge = charge with
            {
                AmountMinor = charge.AmountMinor + convertingAnnual.AmountMinor,
                NetAmountMinor = charge.NetAmountMinor + convertingAnnual.NetAmountMinor,
                TaxAmountMinor = charge.TaxAmountMinor + convertingAnnual.TaxAmountMinor,
                DiscountApplied = charge.DiscountApplied || convertingAnnual.DiscountApplied
            };

            convertingAnnual.IsPrepaid = true;
        }

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
                // Every conversion records what its first paid charge was, stub or whole year — a
                // card-free trial leaves these unset at signup, so this is the only place they can
                // be filled in. A whole period simply records a fraction that is not partial.
                converting ? fraction : null,
                outcome.Value,
                attemptNumber,
                pendingQuantities,
                openingAnnualPeriod is not null,
                convertingAnnual,
                cancellationToken,
                pendingPlan,
                outgoingUsagePeriod);

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
    /// The plan change a schedule puts in force at this boundary, or null when nothing is due.
    /// </summary>
    /// <remarks>
    /// The same "has its instant passed" test <see cref="DueQuantityChange"/> makes, for the same
    /// reason — and it is applied only where that one is: on the success path. A renewal whose
    /// card declines leaves the change pending and the subscriber on the plan they are already
    /// paying for, so dunning retries against the plan that was quoted rather than one nobody has
    /// paid for yet. The first renewal that actually settles is the one that moves them.
    /// </remarks>
    /// <summary>
    /// Whether installing this schedule actually re-anchors metering, rather than replacing it
    /// with an equivalent one.
    /// </summary>
    /// <remarks>
    /// A plan change that keeps the same usage rhythm leaves the open window exactly where it was,
    /// and closing it early would cut a metering period short for no reason — the subscriber's
    /// usage would be rated in two pieces because their <em>fee</em> plan changed.
    /// </remarks>
    private static bool MovesUsageSchedule(BillingSchedule current, BillingSchedule target) =>
        current.Interval != target.Interval ||
        current.IntervalCount != target.IntervalCount ||
        current.AnchorInstantUtc != target.AnchorInstantUtc ||
        !string.Equals(current.TimeZoneId, target.TimeZoneId, StringComparison.Ordinal);

    /// <summary>
    /// The usage window a scheduled plan change is cutting short, queued for rating exactly as an
    /// immediate plan change queues its own.
    /// </summary>
    private async Task<PendingUsagePeriod> SnapshotOutgoingUsagePeriodAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        var periodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc);

        return new PendingUsagePeriod
        {
            PeriodKey = periodKey,
            PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
            PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
            Plan = subscription.Plan,
            Price = subscription.Price,
            CurrencyCode = subscription.CurrencyCode,
            CorrelationId = subscription.CorrelationId,
            MeterAllowances = await MeterAllowanceSnapshot.CaptureAsync(
                subscription,
                new BillingPeriod(
                    0,
                    subscription.CurrentUsagePeriodStartUtc,
                    subscription.CurrentUsagePeriodEndUtc,
                    periodKey),
                _usage,
                _allowances,
                cancellationToken)
        };
    }

    private static PendingPlanChange? DuePlanChange(SubscriptionDetail subscription) =>
        subscription.PendingPlanChange is { } pending &&
        pending.EffectiveAtUtc <= subscription.CurrentPeriodEndUtc
            ? pending
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
    /// <para>
    /// Internal rather than private so <c>SubscriptionCreationService</c>'s purchase preview can
    /// call the exact same resolution to project what a trial's own conversion will actually
    /// charge — passing the trial's own end as <paramref name="nowUtc"/> asks "as of the instant
    /// this trial ends, what would convert." Sharing this one method is what keeps a quote and the
    /// real conversion it previews from ever pricing the same trial two different ways.
    /// </para>
    /// </remarks>
    internal static bool TryResolveTrialConversion(
        SubscriptionDetail subscription,
        DateTime nowUtc,
        out DateTime trialEndUtc,
        out CalendarFirstPeriod stub)
    {
        trialEndUtc = default;
        stub = default;

        if (subscription.InitialChargeAmountMinor is not null ||
            !CalendarBillingAlignment.IsCalendarAligned(subscription.Price) ||
            subscription.Trial is not { EndsAtUtc: var endsAtUtc } ||
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
        PendingAnnualPeriod? annualPeriodToHold,
        CancellationToken cancellationToken,
        PendingPlanChange? appliedPlanChange = null,
        PendingUsagePeriod? outgoingUsagePeriod = null)
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
                QuantityItems = appliedPlanChange?.QuantityItems ?? appliedQuantities,
                ClearPendingQuantityChange = appliedQuantities is not null,
                // The same discipline for a scheduled plan change, and every part of it in this one
                // write: a renewal that installed the plan without its price would bill the new
                // plan at the old rate, and one that installed both without clearing the schedule
                // would install them again next period.
                Plan = appliedPlanChange?.Plan,
                Price = appliedPlanChange?.Price,
                FeeSchedule = appliedPlanChange?.FeeSchedule,
                UsageSchedule = appliedPlanChange?.UsageSchedule,
                ClearPendingPlanChange = appliedPlanChange is not null,
                // Queued in the same write that re-anchors metering, so there is no window in
                // which the schedule has moved and the period it replaced was never captured.
                OutgoingUsagePeriod = outgoingUsagePeriod,
                // Opening the year and forgetting that it was pending must be one write, or the
                // next sweep finds it again and charges for the same year twice.
                ClearPendingAnnualPeriod = openedAnnualPeriod,
                PendingAnnualPeriod = annualPeriodToHold,
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

        if (_documents is not null && paymentDetailId is { Length: > 0 } invoiced)
        {
            // After the period is opened and the charge recorded, so the invoice can only describe a
            // renewal that actually happened. A renewal that charged nothing — fully credited, fully
            // discounted — has no payment and therefore nothing to invoice.
            await _documents.AnnounceChargeAsync(
                subscription,
                invoiced,
                SubscriptionChargeKind.Renewal,
                period.Key,
                subscription.CorrelationId,
                cancellationToken);
        }

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

        if (subscription.Status == SubscriptionStatus.Unpaid)
        {
            // Stays Unpaid rather than advancing to PastDue, which is a live status —
            // TryGetLiveAsync and the entitlement it feeds both grant access to it, so moving an
            // Unpaid subscription there on a decline would restore paid access to somebody who
            // just failed to pay for it.
            //
            // The attempt count still advances, in an Unpaid-to-Unpaid write of its own. The next
            // charge's idempotency key is derived from it, and leaving it still would give a
            // second attempt — from a genuinely different card the subscriber added after this one
            // was declined — the identical key the first attempt used, so the gateway would replay
            // the stale decline instead of trying the new card at all.
            await ApplyTransitionAsync(
                subscription,
                SubscriptionStatus.Unpaid,
                SubscriptionStatus.Unpaid,
                periodKey,
                attemptNumber,
                new SubscriptionTransition(SubscriptionStatus.Unpaid, SubscriptionStatus.Unpaid)
                {
                    DunningAttemptCount = attemptNumber,
                    Event = _events.CreateRenewalOutcome(
                        subscription,
                        SubscriptionConstants.SubscriptionRenewalFailed,
                        periodKey,
                        attemptNumber,
                        subscription.CorrelationId)
                },
                cancellationToken);

            await AuditAsync(subscription, "StateApplied", "Failed", "recovery_charge_declined",
                null, null, attemptNumber, cancellationToken);

            return;
        }

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
