using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Readiness for the one path subscription background work has.
/// </summary>
/// <remarks>
/// Three separate questions, and all three are asked, because answering fewer of them was a real
/// defect rather than a simplification:
/// <list type="number">
/// <item>Is the queue's root database reachable, with the indexes a claim needs?</item>
/// <item>Is any drainer <em>alive</em>?</item>
/// <item>Are its claims <em>getting through</em>?</item>
/// </list>
/// <para>
/// The first version asked only the first, plus a per-process readiness object. That object is
/// written by the drainer in the Worker process and this check runs in the Api, which shares no
/// memory with it &#8212; so it was always in its pristine starting state, and the endpoint reported
/// "subscription background work is draining" on the strength of the Api's own ability to reach
/// MongoDB. Every Worker replica could have been dead, with renewals and invoices piling up
/// unclaimed, and it would still have said so.
/// </para>
/// <para>
/// Liveness now comes from <see cref="ISubscriptionQueueWorkerRegistry"/>, which each drainer writes
/// to in the same root database the queue is in, judged against the database's own clock. That is the
/// only signal that crosses the process boundary, and it is the one that answers the question the
/// endpoint is named for.
/// </para>
/// <para>
/// A live replica whose claims are failing and no replica at all are reported differently on purpose:
/// one sends somebody to the database, the other to the deployment.
/// </para>
/// </remarks>
public sealed class SubscriptionQueueHealthCheck : IHealthCheck
{
    private readonly ISubscriptionWorkQueue _queue;
    private readonly ISubscriptionQueueWorkerRegistry _workers;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly TimeProvider _time;

    public SubscriptionQueueHealthCheck(
        ISubscriptionWorkQueue queue,
        ISubscriptionQueueWorkerRegistry workers,
        IOptionsMonitor<SubscriptionOptions> options,
        TimeProvider? time = null)
    {
        _queue = queue;
        _workers = workers;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var livenessWindow = TimeSpan.FromSeconds(Math.Max(
            30,
            options.SchedulerWorkerLivenessSeconds));
        var claimWindow = TimeSpan.FromSeconds(Math.Max(
            30,
            options.SchedulerWorkerClaimWindowSeconds));

        var probe = await _queue.ProbeAsync(cancellationToken);

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rootDatabaseReachable"] = probe.RootDatabaseReachable,
            ["claimQueryable"] = probe.ClaimQueryable,
            ["missingIndexes"] = probe.MissingIndexes,
            ["queueExecution"] = "mandatory"
        };

        // Connectivity first: without it the fleet reading below cannot be trusted either, since the
        // registry lives in the same database.
        if (!probe.RootDatabaseReachable)
        {
            return HealthCheckResult.Unhealthy(
                "The subscription work queue's root database is unreachable from this process, so " +
                "no subscription background work can run. Nothing falls back to executing it " +
                "directly.",
                data: data);
        }

        if (probe.MissingIndexes.Count > 0)
        {
            // Refused rather than tolerated. Draining without the occurrence index risks two items
            // for one billing period, which is worse than draining nothing: nothing is visible and
            // recoverable, a double charge is neither.
            return HealthCheckResult.Unhealthy(
                "The subscription work queue is missing indexes it needs before it may be " +
                    $"drained: {string.Join(", ", probe.MissingIndexes)}.",
                data: data);
        }

        if (!probe.ClaimQueryable)
        {
            return HealthCheckResult.Unhealthy(
                $"The subscription work queue cannot be queried for due work: {probe.Error}",
                data: data);
        }

        SubscriptionQueueFleetHealth fleet;

        try
        {
            fleet = await _workers.DescribeFleetAsync(
                livenessWindow,
                claimWindow,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unhealthy, not healthy. Being unable to tell whether anything is draining is not
            // evidence that something is, and treating it as such is the whole bug this replaces.
            data["workerRegistryError"] = exception.Message;

            return HealthCheckResult.Unhealthy(
                "Whether any subscription work drainer is alive could not be established, so " +
                "whether work is being executed is unknown.",
                data: data);
        }

        data["liveWorkers"] = fleet.LiveWorkers;
        data["drainingWorkers"] = fleet.DrainingWorkers;
        data["newestHeartbeatAtUtc"] = fleet.NewestHeartbeatAtUtc?.ToString("O") ?? "never";
        data["newestClaimAtUtc"] = fleet.NewestClaimAtUtc?.ToString("O") ?? "never";
        data["worstConsecutiveFailures"] = fleet.WorstConsecutiveFailures;
        data["workerLivenessSeconds"] = (long)livenessWindow.TotalSeconds;

        if (fleet.LiveWorkers == 0)
        {
            return HealthCheckResult.Unhealthy(
                "No subscription work drainer has reported in for " +
                    $"{livenessWindow.TotalSeconds:F0}s. The queue is reachable, which is not the " +
                    "same as being drained: renewals, usage charges and invoices accumulate " +
                    "unclaimed while this is true.",
                data: data);
        }

        if (fleet.DrainingWorkers == 0)
        {
            // Alive and useless. Named apart from "no replicas" because it points somewhere else:
            // the process is up and its claims are not getting through.
            return HealthCheckResult.Unhealthy(
                $"{fleet.LiveWorkers} subscription work drainer(s) are alive but none has claimed " +
                    $"successfully within {claimWindow.TotalSeconds:F0}s" +
                    (fleet.LastFailureClassification is { Length: > 0 } reason
                        ? $": {reason}"
                        : "."),
                data: data);
        }

        if (fleet.WorstConsecutiveFailures > 0)
        {
            // Degraded, not unhealthy: something is draining, and one replica retrying through a
            // failover is not an incident. Paging on it teaches people to ignore the page.
            return HealthCheckResult.Degraded(
                $"{fleet.DrainingWorkers} of {fleet.LiveWorkers} subscription work drainer(s) are " +
                    $"claiming; the worst is retrying after {fleet.WorstConsecutiveFailures} " +
                    "consecutive failures.",
                data: data);
        }

        return HealthCheckResult.Healthy(
            $"{fleet.DrainingWorkers} subscription work drainer(s) are claiming from the durable " +
                "queue.",
            data);
    }
}

/// <summary>
/// Queue connectivity from this process, and nothing about whether work is being drained.
/// </summary>
/// <remarks>
/// Split out and named for what it actually proves. The Api can reach the root database without any
/// drainer existing, and conflating those two was how the endpoint came to report healthy while
/// nothing was being billed. Registered separately so a platform can watch connectivity from the Api
/// while watching liveness where it belongs.
/// </remarks>
public sealed class SubscriptionQueueConnectivityHealthCheck : IHealthCheck
{
    private readonly ISubscriptionWorkQueue _queue;

    public SubscriptionQueueConnectivityHealthCheck(ISubscriptionWorkQueue queue) => _queue = queue;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probe = await _queue.ProbeAsync(cancellationToken);

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rootDatabaseReachable"] = probe.RootDatabaseReachable,
            ["claimQueryable"] = probe.ClaimQueryable,
            ["missingIndexes"] = probe.MissingIndexes,
            ["proves"] = "queue connectivity only, not that anything is draining"
        };

        return probe.IsHealthy
            ? HealthCheckResult.Healthy(
                "The subscription work queue is reachable and claimable from this process. This " +
                    "says nothing about whether a drainer is running.",
                data)
            : HealthCheckResult.Unhealthy(
                probe.Error ?? "The subscription work queue is not usable from this process.",
                data: data);
    }
}
