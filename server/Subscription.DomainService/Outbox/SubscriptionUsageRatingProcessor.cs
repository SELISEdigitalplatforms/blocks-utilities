using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Closes usage periods that have ended and prices their overage into a
/// <see cref="SubscriptionUsageInvoice"/> — the usage clock's own sweep, independent of the fee
/// renewal sweep the way the two schedules themselves are independent.
/// </summary>
/// <summary>
/// Whether <c>EnsureInvoiceAsync</c> left a period actually settled or had to leave it for the
/// next sweep pass.
/// </summary>
/// <remarks>
/// <see cref="Deferred"/> must never be treated as success by a caller: the pending-period
/// snapshot, the closure record and the usage clock all have to stay exactly where they were so
/// the next sweep picks the period up again, the same way it already does for any other
/// not-yet-ready period (a claim still outstanding, a schedule whose time zone briefly failed to
/// resolve). There is no separate retry queue to wire up — the periodic sweep is the retry.
/// </remarks>
public enum InvoiceReadiness
{
    /// <summary>The invoice already existed, or was created just now.</summary>
    Ready,

    /// <summary>
    /// Pricing overflowed a <see cref="long"/> minor-unit amount. No invoice was created, and
    /// nothing about the period was advanced or removed — it is exactly as due as before this
    /// attempt.
    /// </summary>
    Deferred
}

public sealed class SubscriptionUsageRatingProcessor : ISubscriptionUsageRatingProcessor
{
    /// <summary>
    /// Guards the loop that closes every period a long-outaged sweep missed, not just the most
    /// recent one. Bounded the same defensive way <c>BillingPeriodCalculator</c> bounds its own
    /// index correction — a subscription this far behind means something else is wrong.
    /// </summary>
    private const int MaximumPeriodsPerSweep = 24;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly ISubscriptionUsageInvoiceRepository _usageInvoices;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionUsageRatingProcessor> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly TimeProvider _time;
    private readonly ISubscriptionAuditTrail? _audit;
    private readonly IUsagePeriodClosureRepository? _closures;
    private readonly IMeterAllowanceResolver _allowances;

    public SubscriptionUsageRatingProcessor(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        ISubscriptionUsageInvoiceRepository usageInvoices,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionUsageRatingProcessor> logger,
        TimeProvider? time = null,
        ISubscriptionAuditTrail? audit = null,
        ISubscriptionWorkScheduler? scheduler = null,
        ISubscriptionFinancialDocumentAnnouncer? documents = null,
        IUsagePeriodClosureRepository? closures = null,
        IMeterAllowanceResolver? allowances = null)
    {
        _scheduler = scheduler;
        _documents = documents;
        _subscriptions = subscriptions;
        _usage = usage;
        _usageInvoices = usageInvoices;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _audit = audit;
        _closures = closures;
        _allowances = allowances ?? new MeterAllowanceResolver(usage);
    }

    /// <summary>Announces the overage invoice. Optional, like the scheduler beside it.</summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

    public async Task<int> CloseDuePeriodsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _subscriptions.ListDueForUsageRatingAsync(
            tenantId,
            now,
            Math.Max(1, options.UsageRatingBatchSize),
            cancellationToken);

        var closed = 0;

        foreach (var subscription in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(subscription.ItemId)
            });

            await AuditAsync(subscription, "RatingStarted", "InProgress", cancellationToken);
            var subscriptionPeriodsClosed = await CloseSubscriptionAsync(
                subscription, now, cancellationToken);
            closed += subscriptionPeriodsClosed;
            await AuditAsync(subscription, "PeriodsClosed",
                subscriptionPeriodsClosed > 0 ? "Succeeded" : "NoOp", cancellationToken);
        }

        return closed;
    }

    public Task<int> CloseSubscriptionPeriodsAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        CloseSubscriptionAsync(subscription, asOfUtc, cancellationToken);

    public Task ChargeInvoiceAsync(
        SubscriptionUsageInvoice invoice,
        CancellationToken cancellationToken) =>
        ChargeInvoiceAsync(invoice, _options.CurrentValue, _time.GetUtcNow().UtcDateTime, cancellationToken);

    private Task AuditAsync(
        SubscriptionDetail subscription,
        string stage,
        string outcome,
        CancellationToken cancellationToken) =>
        _audit is null ? Task.CompletedTask : _audit.RecordAsync(new SubscriptionAuditEvent
        {
            TenantId = subscription.TenantId,
            OrganizationId = subscription.OrganizationId,
            SubscriptionId = subscription.ItemId,
            OperationId = $"usage-rating:{subscription.ItemId}:{subscription.CurrentUsagePeriodEndUtc:O}",
            CorrelationId = subscription.CorrelationId,
            Operation = "UsageRating",
            Stage = stage,
            Outcome = outcome,
            Source = "Worker",
            CurrencyCode = subscription.CurrencyCode
        }, cancellationToken);

    private async Task<int> CloseSubscriptionAsync(
        SubscriptionDetail subscription,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var periodsClosed = 0;

        // A plan change detaches its outgoing window atomically with the schedule swap. Rate
        // those snapshots first: their counters are still addressed by the old period key and
        // their price/allowance must come from the old plan, not the newly installed one.
        foreach (var pending in subscription.PendingUsagePeriods.ToList())
        {
            // A usage write already admitted against this period — before it was marked Closing,
            // or in the narrow gap while that write was landing — may still be mid-flight. Rating
            // now would price a balance that write is still about to change. Leave the snapshot
            // in PendingUsagePeriods and let the next sweep pass find it again; there is no
            // bespoke retry to schedule, since this loop already runs on its own periodic cadence.
            //
            // Gated only when a closure record actually exists for this period. A plan change's
            // own outgoing window never creates one — closure coordination exists for cancellation
            // specifically — so "no record" there still means "always ready", exactly as before
            // this mechanism existed. A cancellation-originated window always has one by the time
            // it is queued here, since reserving it is what the write that queued it required to
            // succeed; a record that exists but has not yet reached Closing (still CloseReserved,
            // in the brief gap between the subscription transition committing and this loop's own
            // pass, or one from an attempt that never actually committed) must not be rated either
            // — only "Closing, and no writer still holding it open" is actually ready.
            if (_closures is not null)
            {
                var closure = await _closures.GetAsync(
                    subscription.TenantId, subscription.ItemId, pending.PeriodKey, cancellationToken);

                if (closure is not null)
                {
                    // A second, independent signal alongside ActiveWriterCount: a claim stuck
                    // mid-release (ReleasePending — its counter decrement applied or about to,
                    // but the claim itself not yet marked Released) must block rating exactly as
                    // an Active claim does, since a resumed release could still be about to touch
                    // the balance depending on where it crashed.
                    var hasOutstandingClaims = await _closures.HasOutstandingClaimsAsync(
                        subscription.TenantId, subscription.ItemId, pending.PeriodKey, cancellationToken);

                    if ((closure.ActiveWriterCount <= 0) == hasOutstandingClaims)
                    {
                        // The two signals disagree — count says one thing, the claims table
                        // another. Not fatal on its own (a decrement mid-flight in the idempotent
                        // release protocol can transiently look this way), but worth knowing about
                        // if it persists across sweep passes.
                        _logger.LogWarning(
                            "Usage closure signals disagree ActiveWriterCount={ActiveWriterCount} " +
                            "HasOutstandingClaims={HasOutstandingClaims} " +
                            "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey}",
                            closure.ActiveWriterCount,
                            hasOutstandingClaims,
                            PaymentLogValue.Hash(subscription.ItemId),
                            PaymentLogValue.Label(pending.PeriodKey));
                    }

                    if (closure.State != UsagePeriodClosureState.Closing ||
                        closure.ActiveWriterCount > 0 ||
                        hasOutstandingClaims)
                    {
                        continue;
                    }
                }
            }

            var ratingSubscription = new SubscriptionDetail
            {
                ItemId = subscription.ItemId,
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                BillingAccountId = subscription.BillingAccountId,
                Plan = pending.Plan,
                Price = pending.Price,
                CurrencyCode = pending.CurrencyCode,
                CorrelationId = pending.CorrelationId,
                // Best-effort context for IMeterAllowanceResolver, used only as a fallback: the
                // window's own counter (its frozen LimitSnapshot) resolves correctly regardless
                // of these, and pending.MeterAllowances — captured before the cancellation or
                // plan-change transition that cut this window short — takes priority over a live
                // resolve for the crash window where no counter was ever written. These
                // current-not-original Status/Trial/UsageSchedule values are only reached for a
                // legacy PendingUsagePeriod queued before that snapshot existed, or if a meter
                // is somehow missing from the snapshot.
                Status = subscription.Status,
                Trial = subscription.Trial,
                UsageSchedule = subscription.UsageSchedule
            };
            var pendingPeriod = new BillingPeriod(
                0, pending.PeriodStartUtc, pending.PeriodEndUtc, pending.PeriodKey);

            var readiness = await EnsureInvoiceAsync(
                ratingSubscription, pendingPeriod, cancellationToken, pending.MeterAllowances);

            if (readiness == InvoiceReadiness.Deferred)
            {
                // Pricing overflowed and EnsureInvoiceAsync already logged it. Leave the
                // PendingUsagePeriods snapshot and the closure's Closing state exactly as they
                // are — removing either here would make this period vanish with no invoice ever
                // created. The next sweep pass finds this subscription due again and retries it.
                continue;
            }

            // The invoice exists now, so the charge is due now. Announced rather than left for the
            // next sweep: waiting only delays revenue and the subscriber's own record of what they
            // used. Best effort inside the scheduler — the invoice is already written.
            if (_scheduler is not null)
            {
                await _scheduler.ScheduleUsageInvoiceChargeAsync(
                    ratingSubscription,
                    pending.PeriodKey,
                    pending.CorrelationId,
                    cancellationToken);
            }

            await _subscriptions.TryRemovePendingUsagePeriodAsync(
                subscription.TenantId,
                subscription.ItemId,
                pending.PeriodKey,
                cancellationToken);

            if (_closures is not null)
            {
                // The invoice existing is what actually matters financially —
                // EnsureInvoiceAsync's own idempotency already protects a period whose closure
                // record never made it to Closed. Logged rather than silently dropped, since
                // there is no reconciliation sweep yet that would otherwise ever notice a
                // closure stuck short of Closed.
                if (!await _closures.TryMarkClosedAsync(
                        subscription.TenantId, subscription.ItemId, pending.PeriodKey, cancellationToken))
                {
                    _logger.LogWarning(
                        "A rated usage period's closure record could not be marked Closed " +
                        "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey}",
                        PaymentLogValue.Hash(subscription.ItemId),
                        PaymentLogValue.Label(pending.PeriodKey));
                }
            }

            periodsClosed++;
        }

        // The subscription's own live clock only advances while it is still live. A Canceled
        // subscription can only be here because it still holds a PendingUsagePeriods snapshot —
        // handled above — and its own current window was captured into that snapshot at the
        // moment cancellation took effect; advancing it further would rate the same window twice
        // and open a period nothing will ever close.
        if (subscription.Status is not (
            SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            return periodsClosed;
        }

        for (var iteration = 0; iteration < MaximumPeriodsPerSweep; iteration++)
        {
            if (subscription.CurrentUsagePeriodEndUtc > now)
            {
                break;
            }

            var periodKey = PeriodKey.Create(
                subscription.UsageSchedule.Interval,
                subscription.CurrentUsagePeriodStartUtc);
            var currentPeriod = new BillingPeriod(
                0,
                subscription.CurrentUsagePeriodStartUtc,
                subscription.CurrentUsagePeriodEndUtc,
                periodKey);

            var readiness = await EnsureInvoiceAsync(subscription, currentPeriod, cancellationToken);

            if (readiness == InvoiceReadiness.Deferred)
            {
                // Pricing overflowed and EnsureInvoiceAsync already logged it. Do not advance the
                // usage clock — leave the period open so it is reprocessed rather than silently
                // rolled forward past unbilled usage. The next sweep pass (this subscription is
                // still due, since CurrentUsagePeriodEndUtc did not move) retries it.
                break;
            }

            if (!BillingPeriodCalculator.TryGetPeriod(
                    subscription.UsageSchedule,
                    subscription.CurrentUsagePeriodEndUtc,
                    out var nextPeriod))
            {
                _logger.LogError(
                    "A usage period could not be advanced; the schedule's time zone may no " +
                    "longer be valid");

                break;
            }

            var applied = await _subscriptions.TryTransitionAsync(
                subscription.TenantId,
                subscription.ItemId,
                new SubscriptionTransition(subscription.Status, subscription.Status)
                {
                    CurrentUsagePeriodStartUtc = nextPeriod.StartUtc,
                    CurrentUsagePeriodEndUtc = nextPeriod.EndUtc,
                    NextUsageBillingAtUtc = nextPeriod.EndUtc
                },
                cancellationToken);

            if (!applied)
            {
                // Another worker is already advancing this subscription's usage clock.
                break;
            }

            subscription.CurrentUsagePeriodStartUtc = nextPeriod.StartUtc;
            subscription.CurrentUsagePeriodEndUtc = nextPeriod.EndUtc;
            subscription.NextUsageBillingAtUtc = nextPeriod.EndUtc;
            periodsClosed++;
        }

        return periodsClosed;
    }

    /// <summary>
    /// Prices the closed period's overage and records it, unless a crash already left one behind
    /// — <c>TryCreateAsync</c>'s uniqueness index is what makes this safe to call twice.
    /// </summary>
    /// <returns>
    /// <see cref="InvoiceReadiness.Ready"/> once an invoice exists for this period (already did,
    /// or created just now); <see cref="InvoiceReadiness.Deferred"/> if pricing overflowed and no
    /// invoice was created. Callers must treat <see cref="InvoiceReadiness.Deferred"/> as "not
    /// done" — see the type's own remarks.
    /// </returns>
    private async Task<InvoiceReadiness> EnsureInvoiceAsync(
        SubscriptionDetail subscription,
        BillingPeriod period,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, long>? frozenAllowances = null)
    {
        var periodKey = period.Key;
        var existing = await _usageInvoices.GetAsync(
            subscription.TenantId,
            subscription.ItemId,
            periodKey,
            cancellationToken);

        if (existing is not null)
        {
            return InvoiceReadiness.Ready;
        }

        var lines = new List<UsageInvoiceLine>();
        var counters = (await _usage.ListCountersAsync(
                subscription.TenantId,
                subscription.ItemId,
                periodKey,
                cancellationToken))
            .ToDictionary(counter => counter.MeterKey, StringComparer.Ordinal);

        // The ledger is append-first and uniquely keyed; the counter is its fast enforcement
        // projection. A writer can crash after the ledger append but before moving that projection.
        // Final financial rating must therefore sum the durable ledger, not trust a counter which
        // recovery may still be repairing.
        //
        // Every resetting meter is rated here, not just Periodic — CarryForward still resets,
        // rates and reports per window (see MeterResetPolicy.CarryForward's own remarks); only
        // Never sits outside this per-period sweep entirely, since it holds one lifetime balance
        // this processor never closes.
        foreach (var meter in subscription.Plan.Meters.Where(
                     planMeter => planMeter.ResetPolicy != MeterResetPolicy.Never))
        {
            var counter = counters.GetValueOrDefault(meter.MeterKey);
            var ledger = await _usage.SummariseLedgerAsync(
                subscription.TenantId,
                subscription.ItemId,
                meter.MeterKey,
                periodKey,
                cancellationToken);
            var balance = ledger.RecordCount > 0
                ? ledger.Balance
                : counter?.Balance ?? 0;

            // The window's own frozen allowance — a trial grant or a carried-forward allowance
            // included — never the plan's bare IncludedQuantity. The same resolver the overage
            // preview uses, so the two can never disagree about what "overage" means for this
            // window. See UsageChargePreviewParityTests for the coverage this guarantees.
            //
            // For a cut-short (PendingUsagePeriod) window, frozenAllowances — captured before the
            // cancellation/plan-change transition that cut the window short — takes priority over
            // resolving live: the live resolver, given only the counter and a synthetic
            // subscription carrying the *current* (post-transition) status/trial/schedule, cannot
            // reconstruct the original terms for a window whose counter never made it to disk
            // before the crash. A legacy PendingUsagePeriod queued before this snapshot existed
            // carries no frozenAllowances entry, so it falls back to the live resolver exactly as
            // before — same behavior, not a regression for documents already in flight.
            var allowance = frozenAllowances is not null &&
                             frozenAllowances.TryGetValue(meter.MeterKey, out var frozenAllowance)
                ? frozenAllowance
                : await _allowances.EffectiveAsync(
                    subscription, meter, period, counter, cancellationToken);
            var overageQuantity = Math.Max(0, balance - allowance);

            UsageTierAllocationResult allocations;

            try
            {
                allocations = SubscriptionUsageRater.OverageAllocations(
                    meter, overageQuantity, subscription.CurrencyCode);
            }
            catch (OverflowException ex)
            {
                // A technically valid but very large balance or unit rate can make a tier total
                // wrap a plain long — see SubscriptionUsageRater.WalkTierRange's own remarks.
                // There is no HTTP response here to refuse with, the way the preview does, so the
                // whole period is deferred instead: no invoice is created this pass, the same as
                // BillingPeriodCalculator.TryGetPeriod failing above leaves a period for the next
                // sweep rather than persisting a wrapped amount now.
                _logger.LogError(
                    ex,
                    "Usage rating overflowed pricing a meter's overage and deferred the whole " +
                    "period SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} " +
                    "MeterKey={MeterKey}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    PaymentLogValue.Label(periodKey),
                    PaymentLogValue.Label(meter.MeterKey));

                return InvoiceReadiness.Deferred;
            }

            if (allocations.TotalAmountMinor <= 0)
            {
                continue;
            }

            lines.Add(new UsageInvoiceLine
            {
                MeterKey = meter.MeterKey,
                OverageQuantity = overageQuantity,
                AmountMinor = allocations.TotalAmountMinor
            });
        }

        long overage;
        UsageCharge charge;

        try
        {
            // Enumerable.Sum(long) is itself checked and would throw OverflowException here on
            // its own, but the aggregate is summed explicitly so the same overflow handling
            // covers it and the calculator call together.
            overage = lines.Sum(line => line.AmountMinor);

            // Discount and tax, through the calculator shared with the metered overage preview —
            // see UsageChargeCalculator's own remarks for why the two must never drift apart. A
            // gross that fits a long can still overflow once exclusive tax is added on top inside
            // SubscriptionAmountCalculator.TaxBreakdownFor, which is checked for exactly that —
            // the OverflowException it throws is caught here the same as the sum above.
            charge = UsageChargeCalculator.Charge(overage, subscription.Price);
        }
        catch (OverflowException ex)
        {
            _logger.LogError(
                ex,
                "Usage rating overflowed summing or taxing a period's overage and deferred the " +
                "whole period SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey}",
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(periodKey));

            return InvoiceReadiness.Deferred;
        }

        var total = charge.TotalMinor;
        var now = _time.GetUtcNow().UtcDateTime;

        await _usageInvoices.TryCreateAsync(
            new SubscriptionUsageInvoice
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                PeriodKey = periodKey,
                CurrencyCode = subscription.CurrencyCode,
                TotalAmountMinor = total,
                NetAmountMinor = charge.NetMinor,
                TaxAmountMinor = charge.TaxMinor,
                TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
                TaxMode = subscription.Price.TaxMode,
                AutomaticDiscountBasisPoints =
                    SubscriptionDiscountPresentation.RateOf(subscription.Price),
                DiscountAmountMinor = charge.AutomaticDiscountMinor,
                Lines = lines,
                State = total > 0
                    ? SubscriptionUsageInvoiceState.Pending
                    : SubscriptionUsageInvoiceState.NoCharge,
                NextAttemptAtUtc = total > 0 ? now : null,
                CorrelationId = subscription.CorrelationId
            },
            cancellationToken);

        return InvoiceReadiness.Ready;
    }

    public async Task<int> ChargeDueInvoicesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _usageInvoices.ListDueAsync(
            tenantId,
            now,
            Math.Max(1, options.UsageRatingBatchSize),
            cancellationToken);

        foreach (var invoice in due)
        {
            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantHash"] = PaymentLogValue.Hash(tenantId),
                ["SubscriptionHash"] = PaymentLogValue.Hash(invoice.SubscriptionId)
            });

            await ChargeInvoiceAsync(invoice, options, now, cancellationToken);
        }

        return due.Count;
    }

    private async Task ChargeInvoiceAsync(
        SubscriptionUsageInvoice invoice,
        SubscriptionOptions options,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var subscription = await _subscriptions.GetByIdAsync(
            invoice.TenantId,
            invoice.SubscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            // The subscription is gone; there is nothing left this invoice could be applied to.
            await _usageInvoices.TryMarkAbandonedAsync(invoice.TenantId, invoice.ItemId, cancellationToken);

            return;
        }

        var account = await _billingAccounts.GetAsync(
            invoice.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        var attemptNumber = invoice.AttemptCount + 1;

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            await FailAttemptAsync(
                subscription, invoice, attemptNumber, options, now, "no_payment_method", cancellationToken);

            return;
        }

        var outcome = await _gateway.ChargeAsync(
            new SubscriptionChargeRequest
            {
                TenantId = invoice.TenantId,
                // The merchant's scope, not the subscriber's — see BillingAccount.
                OrganizationId =
                    account.ProviderOrganizationId ?? invoice.OrganizationId,
                SubscriberOrganizationId = invoice.OrganizationId,
                ProviderName = account.ProviderName,
                StoredPaymentMethodId = account.DefaultPaymentMethodId,
                ProviderCustomerId = account.ProviderCustomerId,
                AmountMinor = invoice.TotalAmountMinor,
                // From the invoice, which recorded them when it was raised — not from the
                // subscription as it stands now, which may have been repriced since.
                NetAmountMinor = invoice.NetAmountMinor,
                TaxAmountMinor = invoice.TaxAmountMinor,
                TaxRateBasisPoints = invoice.TaxRateBasisPoints,
                TaxMode = invoice.TaxMode,
                // From the invoice, which recorded what it was raised under. No band and no
                // promotion reach a usage invoice, so the built-in reduction is the whole of it.
                GrossAmountMinor = invoice.Lines.Sum(line => line.AmountMinor),
                BuiltInDiscountMinor = invoice.DiscountAmountMinor,
                AutomaticDiscountBasisPoints = invoice.AutomaticDiscountBasisPoints,
                // No combination, deliberately. Nothing was combined: a volume band prices units of
                // a quantity item and a meter has none, so naming one here would report a decision
                // this invoice never made — and possibly the wrong one, since the price's own
                // combination played no part in it.
                CurrencyCode = invoice.CurrencyCode,
                OrderId = SubscriptionConstants.UsageInvoiceOrderIdFor(
                    invoice.SubscriptionId,
                    invoice.PeriodKey),
                Description = "Metered usage overage"
            },
            SubscriptionConstants.UsageInvoiceKeyFor(invoice.SubscriptionId, invoice.PeriodKey, attemptNumber),
            invoice.CorrelationId,
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            _logger.LogWarning(
                "Usage invoice charge declined AttemptNumber={AttemptNumber} Reason={Reason}",
                attemptNumber,
                PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"));

            await FailAttemptAsync(
                subscription, invoice, attemptNumber, options, now,
                outcome.ErrorCode ?? "unknown", cancellationToken);

            return;
        }

        if (!await _usageInvoices.TryMarkChargedAsync(
                invoice.TenantId, invoice.ItemId, outcome.Value!, cancellationToken))
        {
            // Another worker already settled this invoice. Its outcome stands.
            return;
        }

        await _subscriptions.TryAppendEventAsync(
            subscription.TenantId,
            subscription.ItemId,
            _events.CreateUsageRatingOutcome(
                subscription, SubscriptionConstants.UsageRated, invoice.PeriodKey, invoice.CorrelationId),
            cancellationToken);

        if (_documents is not null && outcome.Value is { Length: > 0 } invoiced)
        {
            // Announced after the invoice is marked charged, so the document can only describe an
            // overage this module has finished settling.
            await _documents.AnnounceChargeAsync(
                subscription,
                invoiced,
                SubscriptionChargeKind.Usage,
                invoice.PeriodKey,
                invoice.CorrelationId,
                cancellationToken);
        }

        _logger.LogInformation(
            "Usage invoice charged AttemptNumber={AttemptNumber} PeriodKey={PeriodKey}",
            attemptNumber,
            PaymentLogValue.Label(invoice.PeriodKey));
    }

    /// <summary>
    /// Retries a declined or unpayable invoice up to the attempt ceiling, then abandons it —
    /// never touching the subscription's own status, since this is deliberately a second,
    /// independent invoice from the fee renewal.
    /// </summary>
    private async Task FailAttemptAsync(
        SubscriptionDetail subscription,
        SubscriptionUsageInvoice invoice,
        int attemptNumber,
        SubscriptionOptions options,
        DateTime now,
        string reason,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, options.UsageRatingMaxAttempts);

        if (attemptNumber < maxAttempts)
        {
            await _usageInvoices.RescheduleAsync(
                invoice.TenantId,
                invoice.ItemId,
                attemptNumber,
                now.AddHours(Math.Max(1, options.UsageRatingRetryHours)),
                reason,
                cancellationToken);

            return;
        }

        if (!await _usageInvoices.TryMarkAbandonedAsync(
                invoice.TenantId, invoice.ItemId, cancellationToken))
        {
            return;
        }

        await _subscriptions.TryAppendEventAsync(
            subscription.TenantId,
            subscription.ItemId,
            _events.CreateUsageRatingOutcome(
                subscription, SubscriptionConstants.UsageRatingFailed, invoice.PeriodKey, invoice.CorrelationId),
            cancellationToken);

        _logger.LogWarning(
            "Usage invoice abandoned after every retry PeriodKey={PeriodKey} Reason={Reason}",
            PaymentLogValue.Label(invoice.PeriodKey),
            PaymentLogValue.Label(reason));
    }
}
