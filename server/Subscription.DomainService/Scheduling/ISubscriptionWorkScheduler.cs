using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Schedules subscription background work. The producer half of the queue.
/// </summary>
public interface ISubscriptionWorkScheduler
{
    /// <summary>
    /// Schedules one occurrence, idempotently.
    /// </summary>
    /// <param name="workKey">
    /// Which occurrence this is — a period key, a time bucket. Two calls naming the same occurrence
    /// produce one item, which is what keeps a retried producer from creating a second chance to
    /// charge.
    /// </param>
    /// <returns>True when this call created the occurrence.</returns>
    Task<bool> ScheduleAsync(
        SubscriptionWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default);
}
