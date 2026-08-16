using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One thing a plan permits, in the form the entitlement API answers in.
/// </summary>
/// <remarks>
/// A <see cref="EntitlementLimitKind.Count"/> entitlement names the meter that draws it down;
/// the two are separate because a meter can be recorded without gating anything, and an
/// entitlement can gate without counting.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanEntitlement
{
    /// <summary>The product's own key, such as <c>pep_screening</c>. Never interpreted here.</summary>
    public string Key { get; set; } = string.Empty;

    public EntitlementLimitKind LimitKind { get; set; } =
        EntitlementLimitKind.Boolean;

    /// <summary>The cap, when the kind is a count.</summary>
    public long? Limit { get; set; }

    /// <summary>The meter that draws this entitlement down, when the kind is a count.</summary>
    public string? MeterKey { get; set; }

    public string? UnitLabel { get; set; }
}
