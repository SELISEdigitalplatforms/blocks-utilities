using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The running total for one subscription, meter and usage period.
/// </summary>
/// <remarks>
/// A derived read model, kept so entitlement is a point read rather than an aggregation over
/// the ledger. The ledger stays authoritative: if the two ever disagree the counter is rebuilt,
/// never the other way round.
/// <para>
/// The identifier is composed rather than random, so the counter for a given period can be
/// found — and created — by a single upsert with no prior read, and so crossing a period
/// boundary simply addresses a different document instead of needing a rollover job.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageCounter
{
    /// <summary><c>{subscriptionId}:{meterKey}:{periodKey}</c>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    /// <summary>
    /// How many ledger entries are reflected in the balance. Disagreement with the ledger's own
    /// count is what tells the repair sweep this counter needs recomputing.
    /// </summary>
    public long AppliedRecordCount { get; set; }

    /// <summary>
    /// The allowance as it stood when this period opened. Copied so that editing a plan
    /// mid-period cannot re-fire thresholds that have already been reported.
    /// </summary>
    public decimal? LimitSnapshot { get; set; }

    /// <summary>Threshold percentages already reported. The deduplication authority for alerts.</summary>
    public List<int> NotifiedThresholds { get; set; } = [];

    public DateTime PeriodStartUtc { get; set; }

    public DateTime PeriodEndUtc { get; set; }

    /// <summary>When this derived document may be discarded. The ledger behind it is kept.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey) =>
        $"{subscriptionId}:{meterKey}:{periodKey}";
}
