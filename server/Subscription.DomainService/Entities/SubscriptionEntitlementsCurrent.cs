using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The published entitlement terms of one subscription, shaped for reading.
/// </summary>
/// <remarks>
/// A denormalized copy of <see cref="Plan.Entitlements"/> and <see cref="Plan.FeaturesJson"/>,
/// published whenever <see cref="IUsageProjectionPublisher.RefreshAsync"/> runs — the same trigger
/// that keeps <see cref="SubscriptionUsageCurrent"/> current, for the same reason: it lets a consumer
/// with no API access, only read access to Mongo, answer "what can this subscription do?" with one
/// point read instead of resolving the subscription and joining its plan.
/// <para>
/// One row per subscription, not per meter: an entitlement's terms — its key, its kind, its cap —
/// are plan-wide, and a plan with no metered entitlement at all still has to be discoverable here,
/// which a field folded onto <see cref="SubscriptionUsageCurrent"/> could never be for such a plan.
/// </para>
/// <para>
/// <b>It is not an authority and must never be used as one.</b> For a <see cref="EntitlementLimitKind.Count"/>
/// entitlement the balance drawn against it lives on <see cref="SubscriptionUsageCurrent"/>, and only
/// recording usage through the enforcement path settles a race over it. This document answers "what
/// are the terms", never "how much is left" — a reader after that still needs the meter's own
/// projection, or the API, for the balance.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionEntitlementsCurrent
{
    /// <summary>The subscription's own id. One document per subscription.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// The subscription's status when this was published, so a reader can tell a live grant from
    /// one frozen by cancellation without joining to the subscription.
    /// </summary>
    public SubscriptionStatus SubscriptionStatus { get; set; }

    public string PlanId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    /// <summary>
    /// The plan's own feature bag, verbatim. Stored, versioned and served; never interpreted here —
    /// see <see cref="Plan.FeaturesJson"/>. This is what lets one product ship flags another has
    /// never heard of, for a reader with no API to ask.
    /// </summary>
    public string? FeaturesJson { get; set; }

    /// <summary>Every entitlement the plan grants, in plan order.</summary>
    public List<SubscriptionEntitlementCurrentItem> Entitlements { get; set; } = [];

    /// <summary>
    /// <c>SubscriptionDetail.Version</c> at the moment this was published.
    /// </summary>
    /// <remarks>
    /// The whole ordering story: unlike <see cref="SubscriptionUsageCurrent"/>, there is only one
    /// version here, because this document carries nothing a metered recording ever moves — only
    /// terms the subscription's own version already governs.
    /// </remarks>
    public long SubscriptionVersion { get; set; }

    /// <summary>
    /// The shape of this document, so a consumer reading it directly can refuse an unfamiliar one
    /// rather than silently misread a field that changed meaning.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTime UpdatedAtUtc { get; set; }

    public const int CurrentSchemaVersion = 1;
}

/// <summary>One entitlement's terms, copied from <see cref="PlanEntitlement"/>.</summary>
public sealed class SubscriptionEntitlementCurrentItem
{
    /// <summary>The product's own key, such as <c>pep_screening</c>. Never interpreted here.</summary>
    public string Key { get; set; } = string.Empty;

    public EntitlementLimitKind LimitKind { get; set; }

    /// <summary>The cap, when the kind is a count. Never a balance — see this type's remarks.</summary>
    public decimal? Limit { get; set; }

    /// <summary>The meter that draws this entitlement down, when the kind is a count.</summary>
    public string? MeterKey { get; set; }

    public string? UnitLabel { get; set; }
}
