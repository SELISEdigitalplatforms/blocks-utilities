using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Finds subscription work a tenant owes that nothing announced, and announces it.
/// </summary>
/// <remarks>
/// The repair half of the queue, and <strong>only</strong> the repair half: it holds no processor and
/// can therefore charge nobody. That is the design, not an accident of what it happens to call. The
/// sweep this came out of could execute the work itself, selected by configuration, and two executors
/// for one renewal is one renewal charged twice.
/// <para>
/// Lifted out of the hosted service so the guarantee is testable without a timer. A background loop
/// that waits half a minute between passes cannot be asked "and you are sure you did not charge
/// anyone?" in a unit test; this can, by being handed processors that throw if touched.
/// </para>
/// <para>
/// Announcing is idempotent by the queue's occurrence index, so this may safely repeat what a
/// producer at the point of change already announced, or what another replica's sweep announced a
/// moment ago. Executing would not be.
/// </para>
/// </remarks>
public sealed class SubscriptionRepairAnnouncer
{
    private readonly ISubscriptionWorkScheduler _scheduler;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionPaymentLinkRepository _links;
    private readonly ISubscriptionUsageInvoiceRepository _invoices;
    private readonly ISubscriptionInvoiceHistoryRepository _charges;
    private readonly ISubscriptionFinancialDocumentRepository _documents;
    private readonly ISubscriptionDocumentCursorRepository _cursors;
    private readonly ISubscriptionCancellationService _cancellation;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionRepairAnnouncer> _logger;
    private readonly TimeProvider _time;

    public SubscriptionRepairAnnouncer(
        ISubscriptionWorkScheduler scheduler,
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionUsageInvoiceRepository invoices,
        ISubscriptionInvoiceHistoryRepository charges,
        ISubscriptionFinancialDocumentRepository documents,
        ISubscriptionDocumentCursorRepository cursors,
        ISubscriptionCancellationService cancellation,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionRepairAnnouncer> logger,
        TimeProvider? time = null)
    {
        _scheduler = scheduler;
        _subscriptions = subscriptions;
        _links = links;
        _invoices = invoices;
        _charges = charges;
        _documents = documents;
        _cursors = cursors;
        _cancellation = cancellation;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Enqueues one occurrence per work type this tenant owes. The only thing this sweep does.
    /// </summary>
    /// <remarks>
    /// Bucketed by wall clock rather than keyed per pass, so a sweep that overlaps itself — or two
    /// workers sweeping the same roster — produces one item per bucket rather than one per pass. The
    /// unique occurrence index does the rest, which is what makes a repeated announcement free.
    /// <para>
    /// This repair pass deliberately performs the tenant queries: it runs infrequently and exists
    /// only to heal point-of-change scheduling writes that were lost. Writing one empty queue item
    /// per work type per tenant per bucket would put the roster scan back into the production path
    /// and make an idle fleet look busy forever.
    /// </para>
    /// </remarks>
    public async Task<int> AnnounceAsync(string tenantId, CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        // Reconciled directly rather than only announced as queued work: fixing a closure stuck
        // short of Closing moves no money and changes no subscriber's bill, so it does not need
        // this sweep's own "never executes financial work directly" discipline — see this class's
        // own remarks — and a dedicated queue handler would only add a hop for something this
        // cheap to just do inline.
        var reconciledClosures = await _cancellation.ReconcileStaleClosuresAsync(tenantId, stoppingToken);

        if (reconciledClosures > 0)
        {
            _logger.LogInformation(
                "Repair sweep reconciled stale usage closure reservations " +
                "ReconciledCount={ReconciledCount} TenantId={TenantId}",
                reconciledClosures,
                PaymentLogValue.Id(tenantId));
        }

        var bucketMinutes = Math.Max(1, options.SchedulerSweepBucketMinutes);
        var bucket = new DateTime(
            now.Year, now.Month, now.Day, now.Hour,
            now.Minute / bucketMinutes * bucketMinutes,
            0,
            DateTimeKind.Utc);

        var workKey = $"sweep:{bucket:yyyyMMddTHHmmZ}";
        var dueWorkTypes = await FindDueWorkTypesAsync(tenantId, now, options, stoppingToken);

        if (dueWorkTypes.Count == 0)
        {
            return 0;
        }

        // Minted, not carried — and this is the one place in the chain where that is unavoidable.
        // The sweep is not acting on anybody's request; it is looking for work no request produced.
        // Logged as minted, with what caused it, so a reader who follows a correlation id here and
        // finds no upstream knows that is the answer rather than a broken link.
        var correlationId = $"sweep-{bucket:yyyyMMddTHHmmZ}-{Guid.NewGuid():N}";
        var scheduled = 0;

        foreach (var workType in dueWorkTypes)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return scheduled;
            }

            if (await _scheduler.ScheduleAsync(
                    workType,
                    tenantId,
                    workKey,
                    bucket,
                    correlationId,
                    cancellationToken: stoppingToken))
            {
                scheduled++;
            }
        }

        if (scheduled > 0)
        {
            _logger.LogInformation(
                "Repair sweep announced subscription work AnnouncedCount={ScheduledCount} " +
                "WorkKey={WorkKey} CorrelationId={CorrelationId} CorrelationOrigin={Origin} " +
                "TenantId={TenantId}",
                scheduled,
                workKey,
                correlationId,
                // Says outright that this correlation begins here. Anything downstream carries it,
                // so the trail leads back to this line and stops — which is the truth.
                "MintedByRepairSweep",
                PaymentLogValue.Id(tenantId));
        }

        return scheduled;
    }

    private async Task<IReadOnlyCollection<SubscriptionWorkType>> FindDueWorkTypesAsync(
        string tenantId,
        DateTime now,
        SubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        var due = new HashSet<SubscriptionWorkType>();

        if ((await _links.ListDueAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.ActivationSettlement);
        }

        if ((await _subscriptions.ListStaleAsync(
                tenantId,
                SubscriptionStatus.Incomplete,
                now.AddMinutes(-Math.Max(1, options.InitialChargeGraceMinutes)),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.ActivationRecovery);
        }

        if ((await _subscriptions.ListStaleSettlementsAsync(
                tenantId,
                now.AddMinutes(-Math.Max(1, options.SettlementReservationGraceMinutes)),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.SettlementReservationRecovery);
        }

        if ((await _subscriptions.ListDueForRenewalAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.Renewal);
        }

        if ((await _subscriptions.ListDueForCancellationAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.CancellationEffective);
        }

        if ((await _subscriptions.ListDueForUsageRatingAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.UsagePeriodClosure);
        }

        if ((await _invoices.ListDueAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.UsageInvoiceCharge);
        }

        if ((await _subscriptions.ListWithDueEventsAsync(tenantId, now, 1, cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.OutboxPublication);
        }

        // Does this tenant owe a document? One indexed read against a partial index that holds only
        // the subscriptions currently owing one, so the answer costs the same whether the obligation
        // was recorded a minute ago or a year ago.
        //
        // Deliberately not "has anything settled in the last few hours". That question was here
        // before, and it meant an obligation older than the window never got a sweep scheduled for
        // it — the sweep itself having no window is worth nothing if the thing that wakes it does.
        var owesDocument = (await _subscriptions.ListWithPendingDocumentSourcesAsync(
            tenantId,
            Math.Max(1, options.DocumentDeliveryMaxAttempts),
            1,
            cancellationToken)).Count > 0;

        if (!owesDocument)
        {
            // Nothing recorded, which still leaves the case the record itself was lost. Asked from
            // each sweep's own stored mark rather than from a fixed window, so a charge or refund
            // that arrived during an outage of any length is still seen.
            var settledFrom = await _cursors.GetAsync(
                tenantId,
                SubscriptionFinancialDocumentIssuer.SettledChargeCursor,
                cancellationToken);
            var refundedFrom = await _cursors.GetAsync(
                tenantId,
                SubscriptionFinancialDocumentIssuer.RefundCursor,
                cancellationToken);

            // Asked from the same page position the sweep itself would resume at, so this answers
            // "is there anything the sweep has not accounted for" rather than "has anything happened
            // recently" — the second question is what let an older charge go unswept.
            owesDocument =
                (await _charges.ListSettledSinceAsync(
                    tenantId,
                    settledFrom?.ReadUpToUtc ?? DateTime.MinValue.ToUniversalTime(),
                    settledFrom?.AfterId,
                    1,
                    cancellationToken)).Count > 0 ||
                (await _charges.ListRefundedSinceAsync(
                    tenantId,
                    refundedFrom?.ReadUpToUtc ?? DateTime.MinValue.ToUniversalTime(),
                    refundedFrom?.AfterId,
                    1,
                    cancellationToken)).Count > 0;
        }

        if (owesDocument)
        {
            due.Add(SubscriptionWorkType.FinancialDocumentIssue);
        }

        // Exact, because this one is affordable: the delivery index is partial and holds only the
        // documents that have not reached anybody yet, which is almost none of them.
        if ((await _documents.ListUndeliveredAsync(
                tenantId,
                Math.Max(1, options.DocumentDeliveryMaxAttempts),
                1,
                cancellationToken)).Count > 0)
        {
            due.Add(SubscriptionWorkType.FinancialDocumentDelivery);
        }

        return due;
    }
}
