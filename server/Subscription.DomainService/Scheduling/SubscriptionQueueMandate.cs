using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Says, once per process, that the durable queue is the only way subscription work runs.
/// </summary>
/// <remarks>
/// There is no longer a mode to choose. Every renewal, activation recovery, settlement recovery,
/// usage charge, outbox publication, invoice issue and invoice delivery executes from a claimed
/// queue item, and the reconciliation sweep may only discover work and enqueue it.
/// <para>
/// This exists for the deployment that still carries the old settings. <c>SchedulerEnabled</c> and
/// <c>SchedulerCoordinationEnabled</c> stay bindable for one release so a rollout does not fail on
/// an unknown key, but neither is obeyed &#8212; and a setting that is quietly ignored is worse than
/// one that is rejected, because the operator goes on believing it did something. So a process that
/// still sets either of them says so at warning, naming what it read and what it is doing instead.
/// </para>
/// <para>
/// Nothing branches on what this holds. It is a statement, not a switch, and that is the point: the
/// previous version decided who moved money from configuration, and the two answers were "the sweep
/// charges" and "the queue charges". Getting that wrong in either direction charged a subscriber
/// twice or never.
/// </para>
/// </remarks>
public sealed class SubscriptionQueueMandate
{
    public SubscriptionQueueMandate(
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionQueueMandate> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        LegacySchedulerEnabled = options.Value.SchedulerEnabled;
        LegacyCoordinationEnabled = options.Value.SchedulerCoordinationEnabled;

        if (LegacySchedulerEnabled is not null || LegacyCoordinationEnabled is not null)
        {
            logger.LogWarning(
                "Subscription queue execution is mandatory; legacy scheduler settings are ignored " +
                "LegacySchedulerEnabled={LegacySchedulerEnabled} " +
                "LegacyCoordinationEnabled={LegacyCoordinationEnabled}. Remove both from " +
                "configuration: they are accepted for one compatibility release and then rejected. " +
                "Setting SchedulerEnabled=false no longer stops the queue draining, because there " +
                "is nothing else that would run the work.",
                LegacySchedulerEnabled,
                LegacyCoordinationEnabled);

            return;
        }

        logger.LogInformation(
            "Subscription background work executes only through the durable queue. The " +
            "reconciliation sweep discovers and enqueues missing work and never runs it.");
    }

    /// <summary>
    /// What <c>Subscription:SchedulerEnabled</c> was set to, or null when it was not set at all.
    /// </summary>
    /// <remarks>
    /// Nullable so "absent" and "explicitly false" are distinguishable. They mean very different
    /// things to whoever is reading the warning: one is a deployment that has been cleaned up, the
    /// other is an operator who believes they have turned the queue off.
    /// </remarks>
    public bool? LegacySchedulerEnabled { get; }

    public bool? LegacyCoordinationEnabled { get; }
}
