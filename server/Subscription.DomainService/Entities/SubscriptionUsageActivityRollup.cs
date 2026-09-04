using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One subscription and meter's usage for one UTC day, precomputed so a tenant-wide usage report
/// never scans the live ledger.
/// </summary>
/// <remarks>
/// Derived and disposable, exactly like <see cref="SubscriptionUsageCurrent"/>: the ledger
/// (<see cref="SubscriptionUsageRecord"/>) is the only authority, and this document can always be
/// rebuilt from it. Nothing here may ever be treated as the answer to whether usage is allowed —
/// only <c>POST /api/subscription-usage</c> with <c>enforce</c> decides that.
/// <para>
/// Bucketed by <see cref="DayUtc"/>, which is the day the usage <em>occurred</em>
/// (<c>OccurredAtUtc</c>), not the day it was recorded. A late-reported record still lands in the
/// day it happened, which the rollup job achieves by incrementing this bucket regardless of the
/// order records are read in.
/// </para>
/// <para>
/// <see cref="ConsumedQuantity"/> is a signed sum of every ledger entry's <c>Delta</c> folded into
/// this bucket, so a reversal nets out of the day and meter it corrected rather than requiring a
/// second pass.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageActivityRollup
{
    public const int CurrentSchemaVersion = 1;

    /// <summary><c>{tenantId}:{organizationId}:{subscriptionId}:{meterKey}:{yyyyMMdd}</c>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>
    /// Denormalized at rollup time rather than joined from the subscription: a plan change
    /// overwrites the subscription's own current plan fields, so a historical day's bucket would
    /// otherwise report whatever plan is current now instead of the plan actually in force that
    /// day.
    /// </summary>
    public string PlanId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>Midnight UTC of the day this bucket covers.</summary>
    public DateTime DayUtc { get; set; }

    /// <summary>Signed sum of every ledger entry's <c>Delta</c> folded into this bucket.</summary>
    public decimal ConsumedQuantity { get; set; }

    /// <summary>How many ledger entries (consumption and reversal both) fed this bucket.</summary>
    public long EntryCount { get; set; }

    /// <summary>
    /// Consumption by UTC hour of day, index 0-23, for a peak-hour heatmap. Not decremented by a
    /// reversal's own hour — a reversal folds into the bucket's signed total but is not expected
    /// to net out any one hour precisely, since it may be reported at a different hour than the
    /// entry it corrects.
    /// </summary>
    public long[] HourlyQuantity { get; set; } = new long[24];

    /// <summary>
    /// The highest <c>RecordedAtUtc</c> (and, breaking a tie, the highest <c>ItemId</c>) folded
    /// into this bucket so far — not for paging, but so a re-run of the rollup job can tell
    /// whether a given ledger record has already been folded in without re-deriving the whole
    /// bucket from scratch.
    /// </summary>
    public DateTime SourceCursorRecordedAtUtc { get; set; }

    public string SourceCursorItemId { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
}
