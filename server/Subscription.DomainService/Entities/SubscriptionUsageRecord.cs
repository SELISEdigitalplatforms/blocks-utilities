using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One entry in the append-only usage ledger.
/// </summary>
/// <remarks>
/// Never updated in place. A mistake is corrected by a <see cref="UsageEntryType.Reversal"/>
/// entry, so the history can always explain the figure a customer was billed — which a mutable
/// counter alone cannot.
/// <para>
/// The ledger is the truth and is never expired; the counter beside it is a derived read model
/// that can be rebuilt from these rows.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageRecord
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Which usage period this belongs to. Stamped on write so it can never drift.</summary>
    public string PeriodKey { get; set; } = string.Empty;

    public UsageEntryType EntryType { get; set; } = UsageEntryType.Consumption;

    /// <summary>
    /// Signed: consumption raises the balance, a reversal lowers it.
    /// </summary>
    /// <remarks>
    /// Exact decimal rather than binary floating point, so a reversal cancels the entry it
    /// compensates to the last place. A residue left behind by inexact arithmetic would sit in the
    /// customer's balance for the life of the period and be billed.
    /// </remarks>
    public decimal Delta { get; set; }

    /// <summary>
    /// Unique per subscription and meter. The guard against billing a customer twice for one
    /// event when a caller retries, which at-least-once delivery guarantees will happen.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>The entry this one reverses, when it is a correction.</summary>
    public string? CompensatesRecordId { get; set; }

    /// <summary>When the usage happened, which may not be when it was reported.</summary>
    public DateTime OccurredAtUtc { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Bounded free-form context supplied by the calling product. Billing needs a count, not a
    /// dossier — see the module README on keeping identifying detail out of this.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    public string? RecordedByUserId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;
}
