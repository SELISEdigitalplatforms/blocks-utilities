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
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly SubscriptionQueueMandate _mandate;
    private readonly SubscriptionQueueReadiness _readiness;
    private readonly SubscriptionWorkMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionWorkSchedulerBackgroundService> _logger;

    /// <summary>Identifies this worker in a lease, so a stuck item names the pod holding it.</summary>
    private readonly string _workerName =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    public SubscriptionWorkSchedulerBackgroundService(
        ISubscriptionWorkDispatcher dispatcher,
        ISubscriptionWorkQueue queue,
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

        await WaitForIndexesAsync(stoppingToken);

        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _dispatcher.ProcessDueAsync(_workerName, stoppingToken);

                // Reported before the branch below, because an empty batch is the same proof of
                // reachability as a full one and the healthiest state a queue has.
                _readiness.ClaimSucceeded(_time.GetUtcNow().UtcDateTime);
                failures = 0;

                if (processed > 0)
                {
                    _logger.LogInformation(
                        "Subscription work batch drained ProcessedCount={ProcessedCount}",
                        processed);

                    // Straight back for the next batch while there is a backlog: sleeping a full
                    // interval between batches is what turns a burst into a queue.
                    continue;
                }

                await ReportDepthAsync(stoppingToken);
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

        var now = _time.GetUtcNow().UtcDateTime;
        var alertAfter = TimeSpan.FromSeconds(
            Math.Max(60, _options.CurrentValue.SchedulerUnclaimedAlertSeconds));

        foreach (var depth in depths.Where(entry => entry.Count > 0))
        {
            var age = depth.OldestDueAtUtc is { } oldest ? now - oldest : TimeSpan.Zero;

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
