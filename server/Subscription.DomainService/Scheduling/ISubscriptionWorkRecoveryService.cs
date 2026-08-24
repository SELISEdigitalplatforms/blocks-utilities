using Subscription.DomainService.Services;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// What an operator can do about work the scheduler gave up on.
/// </summary>
public interface ISubscriptionWorkRecoveryService
{
    /// <summary>The caller's own tenant's dead letters, newest first.</summary>
    Task<SubscriptionOperationResult<IReadOnlyList<DeadLetteredWorkResponse>>> ListAsync(
        int limit,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Puts one back in the queue. The handler still decides whether it is due.</summary>
    Task<SubscriptionOperationResult<DeadLetteredWorkResponse>> RequeueAsync(
        string workItemId,
        string reason,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Sets one aside for good, with the reason.</summary>
    Task<SubscriptionOperationResult<DeadLetteredWorkResponse>> AbandonAsync(
        string workItemId,
        string reason,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// A dead letter as an operator needs to see it.
/// </summary>
/// <remarks>
/// Enough to decide without opening a database: what the work was, what it was about, how many
/// attempts it had, why it stopped, and how old it is.
/// </remarks>
public sealed class DeadLetteredWorkResponse
{
    public string WorkItemId { get; init; } = string.Empty;

    public string WorkType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>Which occurrence — a billing period, a reservation, a usage window.</summary>
    public string WorkKey { get; init; } = string.Empty;

    public string? SubscriptionId { get; init; }

    public string? OrganizationId { get; init; }

    public int AttemptCount { get; init; }

    public int MaxAttempts { get; init; }

    public string? LastErrorCode { get; init; }

    public string? LastErrorMessage { get; init; }

    public DateTime DueAtUtc { get; init; }

    public DateTime LastTriedAtUtc { get; init; }

    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// How long this has been due.
    /// </summary>
    /// <remarks>
    /// Stated rather than left to be worked out from two timestamps, because it is the number that
    /// should give somebody pause: requeueing a month-old renewal is rarely what anyone means.
    /// </remarks>
    public long AgeSeconds { get; init; }
}
