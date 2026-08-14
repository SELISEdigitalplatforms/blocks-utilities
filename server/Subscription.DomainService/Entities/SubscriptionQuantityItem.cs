using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// How many of a quantity item this subscription holds, and what each was sold at.
/// </summary>
/// <remarks>
/// The amount is snapshotted alongside the quantity so adding units later charges the price the
/// subscriber agreed to, not today's. Quantity and price are separate decisions and changing
/// one must not move the other.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionQuantityItem
{
    public string ItemKey { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public long Quantity { get; set; }

    public long UnitAmountMinor { get; set; }
}
