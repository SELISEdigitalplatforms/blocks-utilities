using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionUsageRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a ledger entry, returning false when this idempotency key has already been used.
    /// </summary>
    /// <remarks>
    /// Written before the counter moves, deliberately. A crash between the two leaves the
    /// counter under-counting, which the repair sweep can correct from the ledger; the other
    /// order would over-count, and there would be nothing left to prove it.
    /// </remarks>
    Task<bool> TryAppendRecordAsync(
        SubscriptionUsageRecord record,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a delta to the period's counter and returns the counter as it now stands.
    /// </summary>
    /// <remarks>
    /// One upsert with an atomic increment: no read, no rollover job, and no window in which
    /// two callers can both act on the same figure. Crossing a period boundary simply addresses
    /// a different document.
    /// </remarks>
    Task<SubscriptionUsageCounter> ApplyDeltaAsync(
        SubscriptionUsageCounter seed,
        long delta,
        CancellationToken cancellationToken);

    Task<SubscriptionUsageCounter?> GetCounterAsync(
        string tenantId,
        string counterId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionUsageCounter>> ListCountersAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that a threshold has been reported, returning true only for the caller that got
    /// there first — which is the one that should raise the event.
    /// </summary>
    Task<bool> TryMarkThresholdNotifiedAsync(
        string tenantId,
        string counterId,
        int thresholdPercent,
        CancellationToken cancellationToken);

    Task<SubscriptionUsageRecord?> FindRecordByKeyAsync(
        string tenantId,
        string subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The ledger's own view of a period, used to rebuild a counter that has drifted.</summary>
    Task<(long Balance, long RecordCount)> SummariseLedgerAsync(
        string tenantId,
        string subscriptionId,
        string meterKey,
        string periodKey,
        CancellationToken cancellationToken);

    Task<bool> TryRepairCounterAsync(
        string tenantId,
        string counterId,
        long balance,
        long appliedRecordCount,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionUsageRecord>> ListRecordsAsync(
        string tenantId,
        string subscriptionId,
        string? meterKey,
        string? periodKey,
        int limit,
        CancellationToken cancellationToken);
}
