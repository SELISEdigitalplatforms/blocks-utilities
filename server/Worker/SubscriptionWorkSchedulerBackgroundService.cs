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
    private readonly ILogger<SubscriptionWorkSchedulerBackgroundService> _logger;

    /// <summary>Identifies this worker in a lease, so a stuck item names the pod holding it.</summary>
    private readonly string _workerName =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    public SubscriptionWorkSchedulerBackgroundService(
        ISubscriptionWorkDispatcher dispatcher,
        ISubscriptionWorkQueue queue,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionWorkSchedulerBackgroundService> logger)
    {
        _dispatcher = dispatcher;
        _queue = queue;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.SchedulerEnabled)
        {
            // Read once at startup on purpose. Turning the queue on flips the sweep from executing
            // to scheduling, and having the two disagree mid-flight is how work runs twice.
            _logger.LogInformation(
                "Subscription work scheduler is disabled; the reconciliation sweep is executing work");

            return;
        }

        _logger.LogInformation(
            "Subscription work scheduler started WorkerName={WorkerName}",
            PaymentLogValue.Label(_workerName));

        await EnsureIndexesAsync(stoppingToken);

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

    private async Task EnsureIndexesAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _queue.EnsureIndexesAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged loudly rather than fatal. Without the unique occurrence index producing is no
            // longer idempotent, which is worth an alert — but refusing to start would stop the
            // queue draining at all.
            _logger.LogError(
                exception,
                "Subscription work queue indexes could not be created; duplicate occurrences are " +
                "possible until this is resolved");
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
