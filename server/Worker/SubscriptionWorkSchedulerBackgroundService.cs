using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// Drains the durable work queue.
/// </summary>
/// <remarks>
/// The point of this service is what it does <em>not</em> do: walk a roster. A tenant's due renewal
/// used to wait behind every tenant ahead of it in a sequential sweep, thousands of which had
/// nothing to process. Here a claim is one indexed query against one collection, so the wait is
/// proportional to work outstanding rather than to tenants that exist.
/// <para>
/// Runs alongside the reconciliation sweep rather than replacing it. The sweep becomes the repair
/// path — the thing that notices work which was never scheduled because a tenant write committed
/// and the scheduling write did not.
/// </para>
/// </remarks>
public sealed class SubscriptionWorkSchedulerBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 1;

    private readonly ISubscriptionWorkDispatcher _dispatcher;
    private readonly ISubscriptionWorkQueue _queue;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly SubscriptionSchedulerMode _mode;
    private readonly SubscriptionWorkMetrics _metrics;
    private readonly ILogger<SubscriptionWorkSchedulerBackgroundService> _logger;

    /// <summary>Identifies this worker in a lease, so a stuck item names the pod holding it.</summary>
    private readonly string _workerName =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    public SubscriptionWorkSchedulerBackgroundService(
        ISubscriptionWorkDispatcher dispatcher,
        ISubscriptionWorkQueue queue,
        IOptionsMonitor<SubscriptionOptions> options,
        SubscriptionSchedulerMode mode,
        SubscriptionWorkMetrics metrics,
        ILogger<SubscriptionWorkSchedulerBackgroundService> logger)
    {
        _metrics = metrics;
        _dispatcher = dispatcher;
        _queue = queue;
        _options = options;
        _mode = mode;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Announced at warning in both directions, and deliberately loud. The mode is fixed per
        // process, so two replicas can be in different modes during a rolling restart — and the
        // only way to notice that from outside is for every replica to say which one it is in.
        if (!_mode.QueueDriven)
        {
            _logger.LogWarning(
                "Subscription background work mode: DIRECT. The reconciliation sweep executes work " +
                "and the durable queue is not draining. WorkerName={WorkerName}",
                PaymentLogValue.Label(_workerName));

            return;
        }

        _logger.LogWarning(
            "Subscription background work mode: QUEUE. This worker drains the durable queue and the " +
            "sweep only schedules. Enabling this requires a full fleet restart, never a rolling one " +
            "— see Scheduling/README.md. WorkerName={WorkerName}",
            PaymentLogValue.Label(_workerName));

        if (!await WaitForIndexesAsync(stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _dispatcher.ProcessDueAsync(_workerName, stoppingToken);

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
                await Task.Delay(PollInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // One bad pass must not end the loop: the next tick is the recovery for whatever
                // went wrong here, and every claimed item's lease expires back into the queue.
                _logger.LogError(
                    exception,
                    "Subscription work scheduler pass failed and will be retried");

                await Delay(PollInterval(), stoppingToken);
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
    /// transient database problem should not take payment reconciliation down with it.
    /// </para>
    /// </remarks>
    private async Task<bool> WaitForIndexesAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _queue.EnsureIndexesAsync(stoppingToken);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                attempt++;

                var wait = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempt, 6))));

                _logger.LogError(
                    exception,
                    "Subscription work queue indexes could not be created; the scheduler will not " +
                    "claim work until they exist Attempt={Attempt} RetryInSeconds={RetryInSeconds}",
                    attempt,
                    (long)wait.TotalSeconds);

                await Delay(wait, stoppingToken);
            }
        }

        return false;
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

        foreach (var depth in depths.Where(entry => entry.Count > 0))
        {
            _logger.LogInformation(
                "Subscription work queue depth WorkType={WorkType} Status={Status} " +
                "Count={Count} OldestDueAtUtc={OldestDueAtUtc} OldestDueAgeSeconds={AgeSeconds}",
                depth.WorkType,
                depth.Status,
                depth.Count,
                depth.OldestDueAtUtc,
                depth.OldestDueAtUtc is { } oldest
                    ? (long)(DateTime.UtcNow - oldest).TotalSeconds
                    : 0);
        }
    }

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
