using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Reads and writes the precomputed per-subscription, per-meter, per-day usage buckets behind
/// the tenant-admin usage report.
/// </summary>
public interface ISubscriptionUsageActivityRollupRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Folds one ledger entry into its day's bucket, creating the bucket if this is its first
    /// entry.
    /// </summary>
    /// <remarks>
    /// An upsert with <c>$inc</c>, not a read-modify-write: two rollup passes (or a pass and a
    /// concurrent backfill) folding the same tenant's records at once must not lose either one's
    /// contribution. Idempotent by the caller's own bookkeeping — see <c>UsageRollupService</c> —
    /// rather than by this method refusing an entry it has already applied, since the ledger keeps
    /// no per-bucket record of which entries fed it.
    /// </remarks>
    Task ApplyAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        string meterKey,
        string planId,
        string planCode,
        DateTime dayUtc,
        int hourUtc,
        decimal delta,
        DateTime recordedAtUtc,
        string sourceRecordId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<UsageActivityRollupPage> ListAsync(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageRollupCursor? after,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sums consumption across every matching bucket, grouped by the requested granularity —
    /// the volume-over-time report. A Mongo-side aggregation ($dateTrunc + $sum) rather than a
    /// read-and-group in application code, so a tenant with a long history is summed by the
    /// database instead of being paged entirely into this process first.
    /// </summary>
    Task<UsageTimeseriesPage> SumByPeriodAsync(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        BillingInterval granularity,
        int pageSize,
        DateTime? afterPeriodStartUtc,
        CancellationToken cancellationToken);

    /// <summary>Per-organization totals across the matching window — the organization breakdown.</summary>
    Task<UsageOrganizationTotalsPage> SumByOrganizationAsync(
        string tenantId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageOrganizationTotalsCursor? after,
        CancellationToken cancellationToken);
}

public sealed record UsageTimeseriesBucket(
    DateTime PeriodStartUtc,
    decimal ConsumedQuantity,
    long EntryCount);

public sealed record UsageTimeseriesPage(IReadOnlyList<UsageTimeseriesBucket> Items, bool HasMore);

public sealed record UsageOrganizationTotal(
    string OrganizationId,
    decimal ConsumedQuantity,
    long EntryCount);

public sealed record UsageOrganizationTotalsPage(
    IReadOnlyList<UsageOrganizationTotal> Items,
    bool HasMore);

/// <summary>
/// A keyset boundary over totals ordered by descending consumption, then organization id — the
/// tie-break needed because two organizations can consume exactly the same amount.
/// </summary>
public sealed record UsageOrganizationTotalsCursor(decimal ConsumedQuantity, string OrganizationId);

/// <summary>One page of activity-rollup buckets, newest day first.</summary>
public sealed record UsageActivityRollupPage(
    IReadOnlyList<SubscriptionUsageActivityRollup> Items,
    bool HasMore);

/// <summary>A keyset page boundary shared by the rollup listings: the day, then the id.</summary>
public sealed record UsageRollupCursor(DateTime DayUtc, string ItemId);
