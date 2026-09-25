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

    /// <summary>
    /// Which seat this counts for, or null when it counts the subscription's own usage.
    /// </summary>
    /// <remarks>
    /// Null on every counter written before seats existed, and on every organization-wise
    /// subscription for as long as they exist — neither has seats, and neither changes.
    /// </remarks>
    public int? SeatNumber { get; set; }

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

    /// <summary>
    /// The counter for one seat's own window, or the subscription's own when there is no seat.
    /// </summary>
    /// <remarks>
    /// A seat carries its own allowance, so it has to count separately: five seats on a plan
    /// including ten million tokens is ten million each, and one person cannot spend what the other
    /// four were bought.
    /// <para>
    /// A null seat composes exactly the three-part id above, which is what an organization's own
    /// subscription has always used. Every counter already stored keeps its identity, so nothing
    /// needs migrating and no balance moves.
    /// </para>
    /// </remarks>
    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey,
        int? seatNumber) =>
        seatNumber is { } seat
            ? $"{CreateId(subscriptionId, meterKey, periodKey)}:{seat}"
            : CreateId(subscriptionId, meterKey, periodKey);
}
