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
        IUsagePeriodClosureRepository? closures = null)
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
                CorrelationId = pending.CorrelationId
            };

            await EnsureInvoiceAsync(ratingSubscription, pending.PeriodKey, cancellationToken);

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

            await EnsureInvoiceAsync(subscription, periodKey, cancellationToken);

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
    private async Task EnsureInvoiceAsync(
        SubscriptionDetail subscription,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var existing = await _usageInvoices.GetAsync(
            subscription.TenantId,
            subscription.ItemId,
            periodKey,
            cancellationToken);

        if (existing is not null)
        {
            return;
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
        foreach (var meter in subscription.Plan.Meters.Where(
                     planMeter => planMeter.ResetPolicy == MeterResetPolicy.Periodic))
        {
            var ledger = await _usage.SummariseLedgerAsync(
                subscription.TenantId,
                subscription.ItemId,
                meter.MeterKey,
                periodKey,
                cancellationToken);
            var balance = ledger.RecordCount > 0
                ? ledger.Balance
                : counters.GetValueOrDefault(meter.MeterKey)?.Balance ?? 0;

            var amount = SubscriptionUsageRater.OverageAmountMinor(
                meter,
                balance,
                subscription.CurrencyCode);

            if (amount <= 0)
            {
                continue;
            }

            lines.Add(new UsageInvoiceLine
            {
                MeterKey = meter.MeterKey,
                OverageQuantity = Math.Max(0, balance - meter.IncludedQuantity),
                AmountMinor = amount
            });
        }

        var overage = lines.Sum(line => line.AmountMinor);

        // The price's automatic discount applies to what the price charges, and overage is one of
        // the things it charges: a subscriber on an 8%-off yearly price is 8% off on this invoice
        // too. On the aggregate, for the same reason tax is.
        //
        // Through the shared calculator with no band, rather than a percentage worked out here.
        // A volume band prices seats and has no meaning for metered units, so the band is empty —
        // and with no band both combination policies agree, which is why this needs no branch.
        var builtIn = BuiltInDiscountCalculator.Resolve(
            overage,
            new QuantityDiscountOutcome(null, 0, overage, 0, overage),
            subscription.Price.AutomaticDiscountBasisPoints,
            subscription.Price.QuantityDiscountCombination);

        // Tax is on the aggregate, not per meter — the same "one charge, not one per meter"
        // scope this invoice already keeps for the charge itself, and the reason a meter that
        // overages by half a cent cannot cost the subscriber a full one.
        //
        // The rate and mode are the subscription's snapshotted price, so overage is taxed the way
        // the thing it is overage *on* was sold. After the discount, never before: tax is owed on
        // what is actually charged.
        var breakdown = SubscriptionAmountCalculator.TaxBreakdownFor(
            builtIn.SubtotalMinor,
            subscription.Price.TaxRateBasisPoints,
            subscription.Price.TaxMode);

        var total = breakdown.TotalAmountMinor;
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
                NetAmountMinor = breakdown.NetAmountMinor,
                TaxAmountMinor = breakdown.TaxAmountMinor,
                TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
                TaxMode = subscription.Price.TaxMode,
                AutomaticDiscountBasisPoints =
                    SubscriptionDiscountPresentation.RateOf(subscription.Price),
                DiscountAmountMinor = builtIn.DiscountAmountMinor,
                Lines = lines,
                State = total > 0
                    ? SubscriptionUsageInvoiceState.Pending
                    : SubscriptionUsageInvoiceState.NoCharge,
                NextAttemptAtUtc = total > 0 ? now : null,
                CorrelationId = subscription.CorrelationId
            },
            cancellationToken);
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
