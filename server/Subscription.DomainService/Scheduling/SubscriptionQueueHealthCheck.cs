using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Readiness for the one path subscription background work has.
/// </summary>
/// <remarks>
/// Reports unhealthy rather than letting the work happen some other way. There is no other way any
/// more: the sweep may only enqueue, so a process that cannot reach
/// <c>BlocksRootDb.SubscriptionBackgroundWork</c> silently bills nobody. The previous design filled
/// that gap by executing the work in the sweep instead, which is exactly the arrangement that could
/// charge a subscriber twice, so the gap is now reported instead of covered.
/// <para>
/// Two questions, and both are asked. The probe says whether the queue could be drained from here
/// &#8212; root database reachable, required indexes present, the claim query runnable. The readiness
/// state says whether the drainer in <em>this</em> process is actually getting through, which only it
/// can know. A worker whose database answers a probe while every claim throws is unhealthy, and the
/// probe alone would call it fine.
/// </para>
/// <para>
/// Degraded is used for the case that is failing but has not been failing long: a failover drops a
/// pass or two, and paging on the first one trains people to ignore the page. Unhealthy is reserved
/// for a run of failures long enough that somebody has to look.
/// </para>
/// </remarks>
public sealed class SubscriptionQueueHealthCheck : IHealthCheck
{
    /// <summary>
    /// How long a run of failed passes is tolerated before this reports unhealthy.
    /// </summary>
    /// <remarks>
    /// Not configurable on purpose. It answers "is this an incident", which is a property of how
    /// long a database failover takes rather than of any deployment's preference, and a knob here
    /// would be set to whatever silences the alert.
    /// </remarks>
    private static readonly TimeSpan FailureGrace = TimeSpan.FromMinutes(2);

    private readonly ISubscriptionWorkQueue _queue;
    private readonly SubscriptionQueueReadiness _readiness;
    private readonly TimeProvider _time;

    public SubscriptionQueueHealthCheck(
        ISubscriptionWorkQueue queue,
        SubscriptionQueueReadiness readiness,
        TimeProvider? time = null)
    {
        _queue = queue;
        _readiness = readiness;
        _time = time ?? TimeProvider.System;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probe = await _queue.ProbeAsync(cancellationToken);
        var readiness = _readiness.Describe();

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rootDatabaseReachable"] = probe.RootDatabaseReachable,
            ["claimQueryable"] = probe.ClaimQueryable,
            ["missingIndexes"] = probe.MissingIndexes,
            ["drainerIndexesReady"] = readiness.IndexesReady,
            ["drainerConsecutiveFailures"] = readiness.ConsecutiveFailures,
            ["drainerLastClaimAtUtc"] = readiness.LastClaimAtUtc?.ToString("O") ?? "never",
            ["queueExecution"] = "mandatory"
        };

        if (!probe.RootDatabaseReachable)
        {
            return HealthCheckResult.Unhealthy(
                "The subscription work queue's root database is unreachable, so no subscription " +
                "background work can run. Nothing falls back to executing it directly.",
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

        if (readiness.UnhealthySinceUtc is { } since)
        {
            var failingFor = _time.GetUtcNow().UtcDateTime - since;

            return failingFor > FailureGrace
                ? HealthCheckResult.Unhealthy(
                    $"The subscription work drainer has been failing for {failingFor.TotalSeconds:F0}s " +
                        $"across {readiness.ConsecutiveFailures} passes: {readiness.Reason}",
                    data: data)
                : HealthCheckResult.Degraded(
                    $"The subscription work drainer is retrying: {readiness.Reason}",
                    data: data);
        }

        // Deliberately healthy before the first claim. A process that has only just started has not
        // failed at anything, and reporting it unready would fail a rollout on its own start-up.
        return HealthCheckResult.Healthy(
            "Subscription background work is draining from the durable queue.",
            data);
    }
}
