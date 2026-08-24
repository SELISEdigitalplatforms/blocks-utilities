using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// What actually carries out one kind of scheduled work.
/// </summary>
/// <remarks>
/// A handler is handed a claimed item and must re-read the tenant's own state before acting. The
/// scheduling document says only that something was due when it was written; the tenant database
/// says whether it still is. Acting on the first without the second is how work runs twice after a
/// crash, or runs against a subscription that has since been cancelled.
/// </remarks>
public interface ISubscriptionWorkHandler
{
    SubscriptionWorkType WorkType { get; }

    Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken);
}

/// <summary>How an attempt ended, and what the queue should do about it.</summary>
public sealed record SubscriptionWorkOutcome(
    SubscriptionWorkResult Result,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static SubscriptionWorkOutcome Completed() =>
        new(SubscriptionWorkResult.Completed);

    /// <summary>Worth another attempt: a timeout, an unreachable dependency, a lost race.</summary>
    public static SubscriptionWorkOutcome Retry(string errorCode, string errorMessage) =>
        new(SubscriptionWorkResult.Retry, errorCode, errorMessage);

    /// <summary>
    /// Retrying cannot help — the work refers to something that no longer exists, or is refused on
    /// its own terms. Dead-lettered without spending attempts proving it five times.
    /// </summary>
    public static SubscriptionWorkOutcome Permanent(string errorCode, string errorMessage) =>
        new(SubscriptionWorkResult.Permanent, errorCode, errorMessage);
}

public enum SubscriptionWorkResult
{
    Completed = 0,
    Retry = 1,
    Permanent = 2
}
