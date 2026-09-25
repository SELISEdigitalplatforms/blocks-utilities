using MongoDB.Bson.Serialization.Attributes;

using Subscription.DomainService.Enums;

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

    /// <summary>
    /// Whether this item's quantity is how many people may hold a seat.
    /// </summary>
    /// <remarks>
    /// Needed because a plan may sell several quantities — seats and workspaces and whatever else
    /// — and nothing else on an item distinguishes them. <see cref="ItemKey"/> is a free string a
    /// product chose, so reading "seat" out of it would work for one tenant's naming and silently
    /// fail for another's.
    /// <para>
    /// Only meaningful on a <see cref="SubscriberScope.User"/> plan, and only needed when it sells
    /// more than one quantity: a plan with a single item has no ambiguity to resolve, which is what
    /// keeps every plan authored before this from needing an edit.
    /// </para>
    /// </remarks>
    public bool CountsSeats { get; set; }

    /// <summary>
    /// Volume bands, in ascending order. Empty means one price at every quantity.
    /// </summary>
    /// <remarks>
    /// Held on the item rather than the price because the quantity is what selects a band, and the
    /// quantity belongs to the item. A plan sold monthly and annually shares one set of bands
    /// across both prices, which is almost always what an author means.
    /// </remarks>
    public List<QuantityDiscountTier> QuantityDiscountTiers { get; set; } = [];
}
