using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Whether this process is queue-driven, decided once and never again.
/// </summary>
/// <remarks>
/// One value, read from configuration at construction and shared by everything that needs it. The
/// sweep and the scheduler have to agree about which of them executes work, and they cannot agree
/// if each asks configuration separately: a reload between two reads gives one answer to one of
/// them and the other answer to the other.
/// <para>
/// Both wrong answers are damaging, in opposite directions. Flip it on while the scheduler has
/// already decided it is idle, and the sweep schedules work nothing will drain. Flip it off while
/// the scheduler is mid-loop, and the sweep executes work the scheduler is also executing — the
/// same renewal charged twice.
/// </para>
/// <para>
/// So it is deliberately not an <see cref="IOptionsMonitor{T}"/>. Changing the mode takes a
/// restart, which is the honest cost of a switch that decides who moves money.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerMode
{
    public SubscriptionSchedulerMode(
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionSchedulerMode> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        QueueDriven = options.Value.SchedulerEnabled;

        // Logged at startup so the mode a process is running in is answerable from its logs rather
        // than inferred from behaviour, and so a config change that has not been rolled out yet is
        // visibly not in effect.
        logger.LogInformation(
            "Subscription background work mode fixed for this process QueueDriven={QueueDriven}",
            QueueDriven);
    }

    /// <summary>
    /// True when the durable queue executes background work and the sweep only schedules it.
    /// False when the sweep executes work itself, as it did before the queue existed.
    /// </summary>
    public bool QueueDriven { get; }
}
