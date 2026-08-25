using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Utilities;

namespace Worker;

/// <summary>
/// Schedules payment recovery work and drains it.
/// </summary>
/// <remarks>
/// This replaces a safety net that is currently off. `PaymentReconciliationBackgroundService` has its
/// loop commented out and logs only that recovery is disabled, so a payment whose provider call
/// succeeded and whose local write or dispatch was lost stays stuck until somebody notices it by
/// hand. Turning this on restores recovery rather than relocating it.
/// <para>
/// One service produces and drains, unlike the subscription side where a separate sweep produces.
/// There is nothing to hand over from: with the old sweep dead, a second service scheduling work for
/// this one would be two new things where one will do.
/// </para>
/// <para>
/// Producing still walks the tenant roster, which is the thing the queue exists to avoid — but only
/// on the producing pass, and only to write one small document per tenant per bucket. Claiming, which
/// is what a due payment waits on, is one indexed query. Producers at the point of state change would
/// remove the roster walk entirely, and belong with the code that writes payments.
/// </para>
/// </remarks>
public sealed class PaymentWorkSchedulerBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 1;

    private readonly IPaymentBackgroundWorkDispatcher _dispatcher;
    private readonly IPaymentWorkQueue _queue;
    private readonly IPaymentWorkScheduler _scheduler;
    private readonly IPaymentWorkTenantSource _tenants;
    private readonly PaymentSchedulerMode _mode;
    private readonly PaymentWorkMetrics _metrics;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWorkSchedulerBackgroundService> _logger;

    /// <summary>Identifies this worker in a lease, so a stuck item names the pod holding it.</summary>
    private readonly string _workerName = $"{Environment.MachineName}:{Environment.ProcessId}";

    public PaymentWorkSchedulerBackgroundService(
        IPaymentBackgroundWorkDispatcher dispatcher,
        IPaymentWorkQueue queue,
        IPaymentWorkScheduler scheduler,
        IPaymentWorkTenantSource tenants,
        PaymentSchedulerMode mode,
        PaymentWorkMetrics metrics,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWorkSchedulerBackgroundService> logger)
    {
        _dispatcher = dispatcher;
        _queue = queue;
        _scheduler = scheduler;
        _tenants = tenants;
        _mode = mode;
        _metrics = metrics;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Announced at warning in both directions, and deliberately loud: with this off, nothing
        // recovers a payment left behind by a failed dispatch, and that has been true silently.
        if (!_mode.QueueDriven)
        {
            _logger.LogWarning(
                "Payment background work mode: DISABLED. No payment recovery, capture recovery, " +
                "refund recovery or outbox publication runs on a schedule in this process. " +
                "WorkerName={WorkerName}",
                PaymentLogValue.Label(_workerName));

            return;
        }

        _logger.LogWarning(
            "Payment background work mode: QUEUE. This worker schedules and drains payment recovery " +
            "work. Enabling this requires a full fleet restart, never a rolling one — see " +
            "Scheduling/README.md. WorkerName={WorkerName}",
            PaymentLogValue.Label(_workerName));

        if (!await WaitForIndexesAsync(stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScheduleDueWorkAsync(stoppingToken);

                var processed = await _dispatcher.ProcessDueAsync(_workerName, stoppingToken);

                if (processed > 0)
                {
                    _logger.LogInformation(
                        "Payment work batch drained ProcessedCount={ProcessedCount}",
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
                // One bad pass must not end the loop: the next tick is the recovery for whatever
                // went wrong, and every claimed item's lease expires back into the queue.
                _logger.LogError(
                    exception,
                    "Payment work scheduler pass failed and will be retried");

                await Delay(PollInterval(), stoppingToken);
            }
        }

        _logger.LogInformation("Payment work scheduler stopped");
    }

    /// <summary>
    /// Announces one occurrence per work type per tenant, bucketed by wall clock.
    /// </summary>
    /// <remarks>
    /// Bucketed rather than keyed per pass, so a pass that overlaps itself — or two workers on the
    /// same roster — produces one item per bucket rather than one each. The unique occurrence index
    /// does the rest.
    /// <para>
    /// Every type is announced rather than only those with work waiting: deciding that here would
    /// mean the per-tenant queries this exists to avoid. The handlers read tenant state, and an
    /// occurrence with nothing to do costs one claim and one completion.
    /// </para>
    /// </remarks>
    private async Task ScheduleDueWorkAsync(CancellationToken stoppingToken)
    {
        var tenants = await _tenants.ListTenantIdsAsync(stoppingToken);

        if (tenants.Count == 0)
        {
            // A fresh environment, not a failure: nobody has taken a payment yet.
            return;
        }

        var options = _options.CurrentValue;
        var bucketMinutes = Math.Max(1, options.SchedulerBucketMinutes);
        var now = DateTime.UtcNow;
        var bucket = new DateTime(
            now.Year, now.Month, now.Day, now.Hour,
            now.Minute / bucketMinutes * bucketMinutes,
            0,
            DateTimeKind.Utc);

        var workKey = $"sweep:{bucket:yyyyMMddTHHmmZ}";
        var scheduled = 0;

        foreach (var tenantId in tenants)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            // Minted, and this is the one place in the chain where that is unavoidable: nothing here
            // is acting on a request. Named so a reader who follows it back and finds no upstream
            // knows that is the answer rather than a broken link.
            var correlationId = $"paywork-{bucket:yyyyMMddTHHmmZ}-{Guid.NewGuid():N}";

            foreach (var workType in Enum.GetValues<PaymentWorkType>())
            {
                if (await _scheduler.TryScheduleAsync(
                        workType, tenantId, workKey, bucket, correlationId,
                        cancellationToken: stoppingToken))
                {
                    scheduled++;
                }
            }
        }

        if (scheduled > 0)
        {
            _logger.LogInformation(
                "Payment work scheduled ScheduledCount={ScheduledCount} TenantCount={TenantCount} " +
                "WorkKey={WorkKey} CorrelationOrigin={Origin}",
                scheduled,
                tenants.Count,
                workKey,
                "MintedByPaymentScheduler");
        }
    }

    /// <summary>
    /// Blocks until the queue's indexes exist, retrying for as long as it takes.
    /// </summary>
    /// <remarks>
    /// A gate rather than a warning. The occurrence index is what makes producing idempotent, and
    /// without it two producers can create two items for one payment — two chances to recover it,
    /// held apart only by the provider's own idempotency. Draining a queue that may hold duplicates
    /// is worse than draining nothing, because nothing is visible and recoverable.
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
                    "Payment work queue indexes could not be created; the scheduler will not claim " +
                    "work until they exist Attempt={Attempt} RetryInSeconds={RetryInSeconds}",
                    attempt,
                    (long)wait.TotalSeconds);

                await Delay(wait, stoppingToken);
            }
        }

        return false;
    }

    private async Task ReportDepthAsync(CancellationToken stoppingToken)
    {
        var depths = await _queue.DescribeDepthAsync(stoppingToken);

        // Published for the gauges to report. Measured here rather than inside a gauge callback
        // because it is an aggregation over another database, and a collector should not decide when
        // that runs.
        _metrics.RecordDepth(depths);

        foreach (var depth in depths.Where(entry => entry.Count > 0))
        {
            _logger.LogInformation(
                "Payment work queue depth WorkType={WorkType} Status={Status} Count={Count} " +
                "OldestDueAtUtc={OldestDueAtUtc} OldestDueAgeSeconds={AgeSeconds}",
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
