using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>The ledger behind "one trial per plan". See <see cref="TrialUsage"/>.</summary>
public interface ITrialUsageRepository
{
    /// <summary>The subscriber's claim on this plan's trial that is not Released, if any.</summary>
    Task<TrialUsage?> FindActiveAsync(
        string tenantId,
        SubscriberScope scope,
        string subjectId,
        string planCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims the trial for <see cref="TrialUsage.SubscriptionId"/>. False only when a different
    /// subscription already holds it; a retry for the same subscription is success.
    /// </summary>
    Task<bool> TryReserveAsync(TrialUsage usage, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the trial as started for good. Idempotent. Inserts the claim as Used when there is no
    /// reservation to promote — a signup that crashed before reserving, or a plan changed to
    /// mid-trial — and does nothing when someone else already holds that plan's trial.
    /// </summary>
    Task MarkUsedAsync(TrialUsage usage, DateTime usedAtUtc, CancellationToken cancellationToken);

    /// <summary>Claimed -> Released. A no-op on a claim that was already used.</summary>
    Task ReleaseAsync(
        string tenantId, string usageId, DateTime releasedAtUtc, CancellationToken cancellationToken);
}

public static class TrialUsageRepositoryExtensions
{
    /// <summary>
    /// Records that <paramref name="subscription"/> is now trialing its current plan. Called after
    /// the transition commits, never before: a trial is used up because it started, not because
    /// something tried to start it. Tolerates a null repository so callers built without one are
    /// unchanged.
    /// </summary>
    public static async Task MarkTrialStartedAsync(
        this ITrialUsageRepository? repository,
        SubscriptionDetail subscription,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (repository is not null &&
            TrialUsage.For(subscription, subscription.Plan.SubscriberScope, subscription.Plan.Code, nowUtc)
                is { } usage)
        {
            await repository.MarkUsedAsync(usage, nowUtc, cancellationToken);
        }
    }
}
