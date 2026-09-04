namespace Subscription.DomainService.Services;

/// <summary>
/// Precomputes the tenant-wide usage-analytics rollups from the append-only usage ledger.
/// </summary>
/// <remarks>
/// The ledger is the only authority. This reads it forward from a persisted, named cursor —
/// never re-scans it from the beginning — and folds each entry into the day bucket it belongs to,
/// so the tenant-admin usage report never touches the live ledger itself. See
/// <see cref="Entities.SubscriptionUsageActivityRollup"/> and
/// <see cref="Entities.SubscriptionUsageActorRollup"/> for what it writes.
/// </remarks>
public interface IUsageRollupService
{
    /// <summary>
    /// Processes one bounded batch of ledger records and advances the tenant's rollup cursor
    /// past them. Returns how many records were read, which is also the caller's signal for
    /// whether there is more work behind this batch.
    /// </summary>
    Task<int> RunBatchAsync(
        string tenantId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rebuilds every rollup bucket for one subscription from the ledger, from scratch.
    /// </summary>
    /// <remarks>
    /// For repairing a rollup that has drifted, or backfilling history recorded before this
    /// feature existed. Safe to run beside the incremental pass: it reads the same authoritative
    /// ledger and applies through the same idempotent upsert, so it can only converge a bucket
    /// toward the ledger's own truth, never away from it.
    /// </remarks>
    Task<int> BackfillSubscriptionAsync(
        string tenantId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken);
}
