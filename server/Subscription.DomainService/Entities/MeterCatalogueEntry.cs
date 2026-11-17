using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One meter a tenant counts, named once so plans and credit offers can refer to the same thing.
/// </summary>
/// <remarks>
/// Identity and counting semantics only. No price, no expiry, no spend order: the catalogue is
/// reused by every plan and by the standalone credit offer, and each of those prices and expires
/// credits its own way (spec 005). A plan still copies these fields onto its own
/// <see cref="PlanMeter"/> snapshot, so a later rename here reaches new plans only.
/// <para>
/// <see cref="OrganizationId"/> resolves the way <see cref="Plan.OrganizationId"/> does: an
/// organization's own entry wins, otherwise the tenant's. Archived, never deleted, because a plan
/// or an offer may still point at it.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class MeterCatalogueEntry
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    /// <summary>Null is the tenant-wide entry.</summary>
    public string? OrganizationId { get; set; }

    /// <summary>Unique per tenant and organization scope. Never changes once created.</summary>
    public string MeterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    public int QuantityScale { get; set; }

    public CatalogueStatus Status { get; set; } = CatalogueStatus.Active;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; }

    public long Version { get; set; }
}
