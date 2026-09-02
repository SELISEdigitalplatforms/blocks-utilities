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
    /// <summary>
    /// Upper bound of the band, inclusive. Null is the final, unbounded tier.
    /// </summary>
    /// <remarks>
    /// Bands are half-open below and closed above — <c>(previousBound, UpToQuantity]</c> — so a
    /// fractional overage lands in exactly one of them. Whole-unit numbering would leave the space
    /// between one band's bound and the next band's first unit undefined.
    /// </remarks>
    public decimal? UpToQuantity { get; set; }

    public long UnitAmountMinor { get; set; }
}
