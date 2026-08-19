using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Something a tenant sells.
/// </summary>
/// <remarks>
/// <see cref="OrganizationId"/> is nullable and resolves the way payment configuration does:
/// an organization's own plan wins, otherwise the tenant's. Plans are sold *to* organizations
/// *by* the tenant, so requiring one would mean copying every plan for every customer.
/// Subscriptions, usage and entitlement remain strictly organization-scoped — only the
/// catalogue is shared.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class Plan
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string? OrganizationId { get; set; }

    /// <summary>A stable key configuration points at. The display name may change; this may not.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? FamilyCode { get; set; }

    public int? FamilyRank { get; set; }

    public BillingInterval UsageInterval { get; set; } = BillingInterval.Month;

    public int UsageIntervalCount { get; set; } = 1;

    public CatalogueStatus Status { get; set; } = CatalogueStatus.Draft;

    /// <summary>
    /// Free-form plan features as the JSON text they were authored in, stored verbatim and
    /// handed back untouched.
    /// </summary>
    /// <remarks>
    /// Text rather than a parsed document on purpose. Nothing here ever branches on a feature,
    /// so there is nothing to query into, and keeping the original text avoids both the
    /// extended-JSON artefacts a BSON round trip introduces and any temptation to start
    /// interpreting what a product put in here.
    /// </remarks>
    public string? FeaturesJson { get; set; }

    public List<PlanEntitlement> Entitlements { get; set; } = [];

    public List<PlanMeter> Meters { get; set; } = [];

    public List<PlanQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>Trial length in days. Null means the plan offers no trial.</summary>
    public int? TrialDays { get; set; }

    /// <summary>Whether a trial collects a payment method before it starts.</summary>
    public bool TrialRequiresPaymentMethod { get; set; } = true;

    /// <summary>
    /// How much of each meter a trial includes. A free trial of something with a real
    /// per-unit cost is otherwise an open invitation.
    /// </summary>
    public List<TrialMeterGrant> TrialGrants { get; set; } = [];

    /// <summary>Starts at 1: a zero version cannot be told apart from an absent field.</summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
