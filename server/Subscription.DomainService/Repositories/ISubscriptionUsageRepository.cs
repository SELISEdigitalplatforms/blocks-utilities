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
        decimal delta,
        CancellationToken cancellationToken);

    Task<SubscriptionUsageCounter?> GetCounterAsync(
        string tenantId,
        string counterId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads several counters by their composed ids in one round trip.
    /// </summary>
    /// <remarks>
    /// For the current-usage read, which needs one counter per meter. Not expressible through
    /// <see cref="ListCountersAsync"/>: that takes a single period key, and the meters of one
    /// subscription do not share one. A never-reset capacity meter is addressed under
    /// <c>MeterPeriodResolver.LifetimePeriodKey</c> while its periodic neighbours use the billing
    /// schedule's key, so filtering by any single period would silently omit whichever meters do not
    /// use it and report them as unused.
    /// <para>
    /// Missing ids are simply absent from the result. A counter that does not exist yet means no usage
    /// has been recorded in that window, which is a balance of zero rather than an error.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<string, SubscriptionUsageCounter>> GetCountersAsync(
        string tenantId,
        IReadOnlyCollection<string> counterIds,
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
    Task<(decimal Balance, long RecordCount)> SummariseLedgerAsync(
        string tenantId,
        string subscriptionId,
        string meterKey,
        string periodKey,
        CancellationToken cancellationToken);

    Task<bool> TryRepairCounterAsync(
        string tenantId,
        string counterId,
        decimal balance,
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
