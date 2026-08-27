using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// Drains the durable work queue. The only thing that runs subscription background work.
/// </summary>
/// <remarks>
/// The point of this service is what it does <em>not</em> do: walk a roster. A tenant's due renewal
/// used to wait behind every tenant ahead of it in a sequential sweep, thousands of which had
/// nothing to process. Here a claim is one indexed query against one collection, so the wait is
/// proportional to work outstanding rather than to tenants that exist.
/// <para>
/// It starts unconditionally. There is no mode to be in and no configuration that stops it, because
/// nothing else executes this work any more: the reconciliation sweep may only discover work and
/// enqueue it. A setting that could stop this loop would be a setting that stops billing.
/// </para>
/// <para>
/// Runs alongside the reconciliation sweep rather than being replaced by it. The sweep is the repair
/// path — the thing that notices work which was never enqueued because a tenant write committed and
/// the scheduling write did not.
/// </para>
/// </remarks>
public sealed class SubscriptionWorkSchedulerBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 1;

    /// <summary>Longest wait between attempts while the queue is unreachable.</summary>
    /// <remarks>
    /// Bounded so an outage that ends does not leave the fleet idle for another quarter of an hour.
    /// Backing off further would save a database that is already answering nothing, at the cost of
    /// every subscriber whose renewal is waiting.
    /// </remarks>
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(1);

    private readonly ISubscriptionWorkDispatcher _dispatcher;
    private readonly ISubscriptionWorkQueue _queue;
    private readonly ISubscriptionQueueWorkerRegistry _workers;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly SubscriptionQueueMandate _mandate;
    private readonly SubscriptionQueueReadiness _readiness;
    private readonly SubscriptionWorkMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionWorkSchedulerBackgroundService> _logger;

    /// <summary>
    /// Identifies this worker in a lease and in the registry, so a stuck item names the pod holding
    /// it and a heartbeat names the pod that sent it.
    /// </summary>
    /// <remarks>
    /// Carries a per-start suffix. Process ids are reused, and a pod restarting into the same id
    /// would otherwise inherit the dead process's registry record and its failure history.
    /// </remarks>
    private readonly string _workerName = ComposeWorkerName();

    private DateTime _startedAtUtc;
    private DateTime? _lastClaimAtUtc;
    private DateTime? _lastBatchAtUtc;
    private DateTime _lastDepthReportAtUtc = DateTime.MinValue;
    private DateTime _lastHeartbeatAtUtc = DateTime.MinValue;

    public SubscriptionWorkSchedulerBackgroundService(
        ISubscriptionWorkDispatcher dispatcher,
        ISubscriptionWorkQueue queue,
        ISubscriptionQueueWorkerRegistry workers,
        IOptionsMonitor<SubscriptionOptions> options,
        SubscriptionQueueMandate mandate,
        SubscriptionQueueReadiness readiness,
        SubscriptionWorkMetrics metrics,
        ILogger<SubscriptionWorkSchedulerBackgroundService> logger,
        TimeProvider? time = null)
    {
        _metrics = metrics;
        _dispatcher = dispatcher;
        _queue = queue;
        _workers = workers;
        _options = options;
        _mandate = mandate;
        _readiness = readiness;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolved rather than merely injected: constructing it is what logs the mandate, and what
        // warns a deployment that still carries the settings which used to be able to stop this.
        _ = _mandate;

        _logger.LogInformation(
            "Subscription background work drainer started. Queue execution is mandatory; no " +
            "configuration disables it. WorkerName={WorkerName}",
            PaymentLogValue.Label(_workerName));

        _startedAtUtc = _time.GetUtcNow().UtcDateTime;

        // Before the indexes, so a replica that cannot create them is still visible as alive and
        // failing rather than as absent. The two mean different things to whoever is reading.
        await HeartbeatAsync(0, stoppingToken);

        await WaitForIndexesAsync(stoppingToken);

        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _dispatcher.ProcessDueAsync(_workerName, stoppingToken);
                var now = _time.GetUtcNow().UtcDateTime;

                // Recorded before the branch below, because an empty batch is the same proof of
                // reachability as a full one and the healthiest state a queue has.
                _readiness.ClaimSucceeded(now);
                _lastClaimAtUtc = now;
                failures = 0;

                if (processed > 0)
                {
                    _lastBatchAtUtc = now;

                    _logger.LogInformation(
                        "Subscription work batch drained ProcessedCount={ProcessedCount}",
                        processed);
                }

                // Both of these run whether or not the batch was empty, which is the fix for a real
                // gap: depth reporting used to sit after the `continue` below, so a queue with work
                // in it every pass never reported its own backlog. Invoice issue and delivery are
                // the lowest-priority work types, so the shape that hid was exactly the one worth
                // seeing — renewals draining continuously while invoices aged behind them and the
                // dashboard showed the last idle pass's numbers.
                await HeartbeatAsync(0, stoppingToken);
                await ReportDepthIfDueAsync(now, stoppingToken);

                if (processed > 0)
                {
                    // Straight back for the next batch while there is a backlog: sleeping a full
                    // interval between batches is what turns a burst into a queue.
                    continue;
                }

                await Delay(PollInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                failures++;

                // The loop never ends and never falls back to executing the work elsewhere. Every
                // claimed item's lease expires back into the queue, so the cost of a bad pass is
                // delay; the cost of a second executor would be a second charge.
                _readiness.Failed(exception.Message, _time.GetUtcNow().UtcDateTime);

                var wait = Backoff(failures);

                _logger.LogError(
                    exception,
                    "Subscription work scheduler pass failed and will be retried. No work is " +
                    "executed outside the queue, so this delays subscription billing until it " +
                    "recovers Attempt={Attempt} RetryInSeconds={RetryInSeconds}",
                    failures,
                    (long)wait.TotalSeconds);

                // Still reported, and this is the point of a durable registry: a replica whose
                // claims are failing has to look different from one that is gone. Best-effort — the
                // failure above may well be the same database this writes to.
                await HeartbeatAsync(failures, stoppingToken);

                await Delay(wait, stoppingToken);
            }
        }

        _logger.LogInformation("Subscription work scheduler stopped");
    }

    /// <summary>
    /// Blocks until the queue's indexes exist, retrying for as long as it takes.
    /// </summary>
    /// <remarks>
    /// Deliberately a gate rather than a warning. The occurrence index is what makes producing
    /// idempotent, and without it two producers can create two items for the same billing period —
    /// which is two chances to charge, held apart only by the provider's own idempotency. Draining a
    /// queue that may contain duplicates is worse than draining nothing, because nothing is visible
    /// and recoverable while a double charge is neither.
    /// <para>
    /// It retries instead of throwing so the worker's other hosted services keep running: a
    /// transient database problem should not take payment reconciliation down with it. The health
    /// check reports the process unready throughout, which is how the outage becomes visible now
    /// that nothing executes this work directly.
    /// </para>
    /// </remarks>
    private async Task WaitForIndexesAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _queue.EnsureIndexesAsync(stoppingToken);
                _readiness.IndexesReady();

                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                attempt++;
                _readiness.Failed(exception.Message, _time.GetUtcNow().UtcDateTime);

                var wait = Backoff(attempt);

                _logger.LogError(
                    exception,
                    "Subscription work queue indexes could not be created; the scheduler will not " +
                    "claim work until they exist, and nothing else will run the work in the " +
                    "meantime Attempt={Attempt} RetryInSeconds={RetryInSeconds}",
                    attempt,
                    (long)wait.TotalSeconds);

                await Delay(wait, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Reports what is waiting, on an idle pass only.
    /// </summary>
    /// <remarks>
    /// Idle, because a busy worker's own throughput lines already say the queue is moving — and an
    /// aggregation per batch would add load exactly when there is least room for it. What this
    /// catches is the shape nothing else shows: a queue that is not draining because everything in
    /// it is scheduled for later, or dead.
    /// </remarks>
    private async Task ReportDepthAsync(CancellationToken stoppingToken)
    {
        var depths = await _queue.DescribeDepthAsync(stoppingToken);

        // Published for the gauges to report. Measured here rather than inside a gauge callback
        // because it is an aggregation over another database, and a collector should not decide
        // when that runs.
        _metrics.RecordDepth(depths);

        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var depth in depths.Where(entry => entry.Count > 0))
        {
            var age = depth.OldestDueAtUtc is { } oldest ? now - oldest : TimeSpan.Zero;
            var alertAfter = AlertThresholdFor(depth.WorkType, options);

            // Warned rather than merely counted once due work has waited too long. A gauge shows
            // this to whoever is already looking at a dashboard; the point of an alertable line is
            // the case where nobody is.
            if (depth.Status == BackgroundWorkStatus.Pending && age > alertAfter)
            {
                _logger.LogWarning(
                    "Subscription work is due and unclaimed WorkType={WorkType} Count={Count} " +
                    "OldestDueAtUtc={OldestDueAtUtc} OldestDueAgeSeconds={AgeSeconds} " +
                    "ThresholdSeconds={ThresholdSeconds}",
                    depth.WorkType,
                    depth.Count,
                    depth.OldestDueAtUtc,
                    (long)age.TotalSeconds,
                    (long)alertAfter.TotalSeconds);

                continue;
            }

            _logger.LogInformation(
                "Subscription work queue depth WorkType={WorkType} Status={Status} " +
                "Count={Count} OldestDueAtUtc={OldestDueAtUtc} OldestDueAgeSeconds={AgeSeconds}",
                depth.WorkType,
                depth.Status,
                depth.Count,
                depth.OldestDueAtUtc,
                (long)age.TotalSeconds);
        }
    }

    /// <summary>
    /// Publishes this replica's state to the root database, at most once per heartbeat interval.
    /// </summary>
    /// <remarks>
    /// Rate-limited rather than written every pass: a busy drainer loops continuously, and one write
    /// per batch would be a write per few milliseconds for information that changes on the scale of
    /// seconds.
    /// <para>
    /// Best-effort on purpose. A heartbeat that threw would take down the loop that does the actual
    /// billing, to report that the billing is unhealthy. A failed heartbeat already reports itself:
    /// the record stops being recent, which is precisely what the readiness check is looking for.
    /// </para>
    /// </remarks>
    private async Task HeartbeatAsync(int consecutiveFailures, CancellationToken stoppingToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var interval = TimeSpan.FromSeconds(Math.Max(
            5,
            _options.CurrentValue.SchedulerWorkerHeartbeatSeconds));

        // Always on a failure: that is the transition a reader most needs to see promptly.
        if (consecutiveFailures == 0 && now - _lastHeartbeatAtUtc < interval)
        {
            return;
        }

        try
        {
            await _workers.HeartbeatAsync(
                new SubscriptionQueueWorkerBeat(
                    _workerName,
                    _startedAtUtc,
                    _lastClaimAtUtc,
                    _lastBatchAtUtc,
                    consecutiveFailures,
                    consecutiveFailures > 0 ? now : null,
                    // A classification, never the driver's message: this record is readable across
                    // every tenant, and an exception string is where a host name or a connection
                    // string ends up.
                    consecutiveFailures > 0 ? "queue_pass_failed" : null),
                stoppingToken);

            _lastHeartbeatAtUtc = now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Subscription work drainer heartbeat could not be written; readiness will report " +
                "this replica as absent until it can be WorkerName={WorkerName}",
                PaymentLogValue.Label(_workerName));
        }
    }

    /// <summary>
    /// Reports queue depth on a fixed interval, whatever the last batch did.
    /// </summary>
    /// <remarks>
    /// Interval-driven rather than idle-driven. Tied to an empty batch, this never ran while there
    /// was continuously something to claim — so the one queue shape worth alerting on, a backlog
    /// that keeps growing, was the one that produced no fresh numbers.
    /// <para>
    /// Still not every pass: it is an aggregation over another database, and a busy drainer's own
    /// throughput lines already say the queue is moving.
    /// </para>
    /// </remarks>
    private async Task ReportDepthIfDueAsync(DateTime now, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(
            5,
            _options.CurrentValue.SchedulerDepthReportSeconds));

        if (now - _lastDepthReportAtUtc < interval)
        {
            return;
        }

        _lastDepthReportAtUtc = now;

        await ReportDepthAsync(stoppingToken);
    }

    /// <summary>
    /// How long this kind of work may sit unclaimed before it is worth a warning.
    /// </summary>
    /// <remarks>
    /// Tighter for the two document types. They are the lowest-priority work in the queue, so they
    /// are the first to be starved by a sustained backlog of renewals and recovery — and for them
    /// the age <em>is</em> the symptom a subscriber sees: a payment taken with no invoice issued, or
    /// an invoice issued and never delivered. Ordinary repair work being late costs a delay nobody
    /// outside notices.
    /// </remarks>
    private static TimeSpan AlertThresholdFor(
        SubscriptionWorkType workType,
        SubscriptionOptions options) =>
        workType is SubscriptionWorkType.FinancialDocumentIssue
            or SubscriptionWorkType.FinancialDocumentDelivery
            ? TimeSpan.FromSeconds(Math.Max(60, options.SchedulerDocumentUnclaimedAlertSeconds))
            : TimeSpan.FromSeconds(Math.Max(60, options.SchedulerUnclaimedAlertSeconds));

    /// <summary>
    /// Names this replica for a lease and for the registry.
    /// </summary>
    /// <remarks>
    /// The per-start suffix is the load-bearing part: process ids are reused, and a pod restarting
    /// into the same id would otherwise inherit the dead process's registry record and its failure
    /// history — so a replica that had been failing would come back looking as though it still was.
    /// <para>
    /// Bounded rather than sliced. An unconditional slice threw on any host whose machine name left
    /// the composed string under the bound, which is most of them, and it threw in the constructor
    /// — so the drainer would not have started at all.
    /// </para>
    /// </remarks>
    private static string ComposeWorkerName()
    {
        const int maximumLength = 64;
        var name = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        return name.Length <= maximumLength ? name : name[..maximumLength];
    }

    /// <summary>Exponential, capped, so a long outage does not become a long idle period after it.</summary>
    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(
            MaximumBackoff.TotalSeconds,
            5 * Math.Pow(2, Math.Min(attempt, 6))));

    private static async Task Delay(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down between passes is not a failure.
        }
    }

    private TimeSpan PollInterval() =>
        TimeSpan.FromSeconds(
            Math.Max(MinimumPollSeconds, _options.CurrentValue.SchedulerPollSeconds));
}
