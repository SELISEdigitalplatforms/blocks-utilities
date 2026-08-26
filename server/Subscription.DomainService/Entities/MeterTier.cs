using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One band of a graduated rate table.
/// </summary>
/// <remarks>
/// Defined now and priced later: phase 1 records usage but does not rate it. Laying the shape
/// down with the meter keeps a later phase from having to migrate live plans.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class MeterTier
{
    /// <summary>Upper bound of the band. Null is the final, unbounded tier.</summary>
    public long? UpToQuantity { get; set; }

    public long UnitAmountMinor { get; set; }
}
