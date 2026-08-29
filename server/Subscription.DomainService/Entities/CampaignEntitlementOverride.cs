using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A count entitlement's limit, temporarily replaced for as long as a campaign applies.
/// </summary>
/// <remarks>
/// Names one <see cref="PlanEntitlement.Key"/> and one replacement <see cref="Limit"/> — never a
/// list, because every campaign this exists for today overrides exactly one entitlement, and a
/// list would have to answer what happens when two overrides name the same key with no product
/// requirement to answer it from.
/// <para>
/// Every other entitlement the plan grants is untouched: this is a substitution for one key, not
/// a second entitlement set layered over the plan's own.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class CampaignEntitlementOverride
{
    /// <summary>The <see cref="PlanEntitlement.Key"/> this replaces. Never interpreted here.</summary>
    public string EntitlementKey { get; set; } = string.Empty;

    /// <summary>
    /// The replacement cap. Positive, and validated at authoring time to never exceed the plan's
    /// own limit for the same key — a campaign can shrink what a subscriber may use, never grow it
    /// past what the plan they are actually on already grants.
    /// </summary>
    public long Limit { get; set; }
}
