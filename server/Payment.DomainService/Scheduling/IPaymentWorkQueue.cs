using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>The durable queue of payment background work, in the platform's root database.</summary>
public interface IPaymentWorkQueue
{
    /// <summary>
    /// Creates the indexes the queue needs for correctness, not only speed — the unique occurrence
    /// index is what makes producing idempotent.
    /// </summary>
    Task EnsureIndexesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Schedules work, or leaves alone what is already scheduled for the same occurrence.
    /// </summary>
    /// <returns>True when this call created the occurrence.</returns>
    Task<bool> ScheduleAsync(PaymentBackgroundWork work, CancellationToken cancellationToken);

    /// <summary>Claims due work, or work whose lease has expired. One atomic write per item.</summary>
    Task<IReadOnlyList<PaymentBackgroundWork>> ClaimDueAsync(
        string leaseId,
        string leasedBy,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Extends a lease mid-flight. False when the lease is no longer held.</summary>
    Task<bool> RenewLeaseAsync(
        string itemId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        string itemId,
        string leaseId,
        TimeSpan retention,
        CancellationToken cancellationToken);

    /// <summary>Returns work with backoff, or dead-letters it once attempts run out.</summary>
    Task<BackgroundWorkStatus> FailAsync(
        string itemId,
        string leaseId,
        string errorCode,
        string errorMessage,
        bool permanent,
        TimeSpan backoff,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentBackgroundWork>> ListDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken,
        string? tenantId = null);

    Task<IReadOnlyList<PaymentWorkQueueDepth>> DescribeDepthAsync(
        CancellationToken cancellationToken);
}

/// <summary>How much of one kind of work is waiting, and how long the oldest has waited.</summary>
public sealed record PaymentWorkQueueDepth(
    PaymentWorkType WorkType,
    BackgroundWorkStatus Status,
    long Count,
    DateTime? OldestDueAtUtc);
