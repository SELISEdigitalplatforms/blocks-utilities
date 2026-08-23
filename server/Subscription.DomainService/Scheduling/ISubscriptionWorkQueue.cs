using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The durable queue of subscription background work, in the platform's root database.
/// </summary>
public interface ISubscriptionWorkQueue
{
    /// <summary>
    /// Creates the indexes the queue needs for correctness, not only for speed — the unique
    /// occurrence index is what makes producing idempotent.
    /// </summary>
    Task EnsureIndexesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Schedules work, or leaves alone what is already scheduled for the same occurrence.
    /// </summary>
    /// <remarks>
    /// Idempotent by <c>TenantId + WorkType + AggregateId + WorkKey</c>. A producer that runs twice
    /// — because a sweep overlapped, or a caller retried — must not create a second chance to move
    /// the same money.
    /// </remarks>
    /// <returns>True when this call created the occurrence, false when it already existed.</returns>
    Task<bool> ScheduleAsync(SubscriptionBackgroundWork work, CancellationToken cancellationToken);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> items that are due, or whose lease has expired.
    /// </summary>
    /// <remarks>
    /// One atomic compare-and-set per item, so two workers cannot hold the same item at once.
    /// Highest priority first, then longest overdue — a queue that ordered by insertion would let a
    /// backlog of bookkeeping delay a renewal.
    /// </remarks>
    Task<IReadOnlyList<SubscriptionBackgroundWork>> ClaimDueAsync(
        string leaseId,
        string leasedBy,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extends a lease mid-flight, for work that outlives the lease it was claimed under.
    /// </summary>
    /// <returns>False when the lease is no longer held, which means somebody else has it.</returns>
    Task<bool> RenewLeaseAsync(
        string itemId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Marks work done and sets when its record may be purged.</summary>
    Task<bool> CompleteAsync(
        string itemId,
        string leaseId,
        TimeSpan retention,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns work to the queue with backoff, or dead-letters it once attempts run out.
    /// </summary>
    /// <param name="permanent">
    /// True for a failure that retrying cannot fix, which dead-letters immediately rather than
    /// spending attempts proving the same thing five times.
    /// </param>
    Task<BackgroundWorkStatus> FailAsync(
        string itemId,
        string leaseId,
        string errorCode,
        string errorMessage,
        bool permanent,
        TimeSpan backoff,
        CancellationToken cancellationToken);

    /// <summary>Everything dead-lettered, newest first, for the alert and the operator queue.</summary>
    Task<IReadOnlyList<SubscriptionBackgroundWork>> ListDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Queue depth and the oldest due item per work type — what a dashboard needs to show that
    /// work is being drained rather than accumulating.
    /// </summary>
    Task<IReadOnlyList<SubscriptionWorkQueueDepth>> DescribeDepthAsync(
        CancellationToken cancellationToken);
}

/// <summary>How much of one kind of work is waiting, and how long the oldest has waited.</summary>
public sealed record SubscriptionWorkQueueDepth(
    SubscriptionWorkType WorkType,
    BackgroundWorkStatus Status,
    long Count,
    DateTime? OldestDueAtUtc);
