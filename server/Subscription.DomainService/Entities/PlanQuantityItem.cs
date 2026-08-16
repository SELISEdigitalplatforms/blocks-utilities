using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Something a subscriber buys a number of, priced per unit.
/// </summary>
/// <remarks>
/// The platform has no idea what the unit is. One product sells seats, another sells users or
/// workspaces; the label is the product's word and travels through to the caller untouched.
/// A field called <c>Seats</c> here would have made the module one client's billing system.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanQuantityItem
{
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>The product's own word, shown to its users. "seat", "user", "workspace".</summary>
    public string UnitLabel { get; set; } = string.Empty;

    public long MinQuantity { get; set; } = 1;

    public long? MaxQuantity { get; set; }

    public long DefaultQuantity { get; set; } = 1;
}
