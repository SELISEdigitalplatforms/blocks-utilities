using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One user's usage for one organization, meter and UTC day.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="SubscriptionUsageActivityRollup"/> rather than an embedded
/// per-user map on it: an embedded map grows with organization headcount and would eventually
/// threaten Mongo's per-document size limit, where a document per user does not.
/// <para>
/// <see cref="ConsumedQuantity"/> sums <c>RecordedByUserId</c>-attributed ledger entries,
/// reversals included — a reversal is written carrying the same <c>RecordedByUserId</c> as the
/// entry it corrects (see <c>UsageRecordingService.RefuseAsync</c>), so it nets out of the actor
/// who caused it rather than an unattributed bucket.
/// </para>
/// <para>
/// Only the Blocks user id is stored. Display names resolve client-side through IAM, keeping
/// identity out of the billing module.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageActorRollup
{
    public const int CurrentSchemaVersion = 1;

    /// <summary><c>{tenantId}:{organizationId}:{meterKey}:{yyyyMMdd}:{userId}</c>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    public DateTime DayUtc { get; set; }

    public string UserId { get; set; } = string.Empty;

    public decimal ConsumedQuantity { get; set; }

    public long EntryCount { get; set; }

    public DateTime SourceCursorRecordedAtUtc { get; set; }

    public string SourceCursorItemId { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
}
