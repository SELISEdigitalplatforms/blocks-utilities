using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Reads and writes the precomputed per-user, per-meter, per-day usage buckets behind the
/// tenant-admin usage report's actor breakdown.
/// </summary>
public interface ISubscriptionUsageActorRollupRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Folds one ledger entry into its actor's day bucket. See
    /// <see cref="ISubscriptionUsageActivityRollupRepository.ApplyAsync"/> for why this is an
    /// idempotent upsert rather than a read-modify-write.
    /// </summary>
    Task ApplyAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime dayUtc,
        string userId,
        decimal delta,
        DateTime recordedAtUtc,
        string sourceRecordId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<UsageActorRollupPage> ListAsync(
        string tenantId,
        string organizationId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageActorRollupCursor? after,
        CancellationToken cancellationToken);
}

public sealed record UsageActorRollupPage(
    IReadOnlyList<SubscriptionUsageActorRollup> Items,
    bool HasMore);

/// <summary>A keyset page boundary for the actor listing: the day, then the user id.</summary>
public sealed record UsageActorRollupCursor(DateTime DayUtc, string UserId);
