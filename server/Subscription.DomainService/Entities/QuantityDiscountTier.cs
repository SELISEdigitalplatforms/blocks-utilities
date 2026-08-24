using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One volume band: buy this many units and the whole quantity charge is reduced by this much.
/// </summary>
/// <remarks>
/// A band, not a plan. Five seat prices used to mean five plans, which meant five sets of
/// entitlements to keep in step and a plan change every time a company hired someone. The
/// quantity decides the band, so a subscriber crossing one keeps their plan, their snapshot and
/// their billing period.
/// <para>
/// The reduction applies to the entire quantity charge rather than only to the units inside the
/// band. Graduated pricing — where the first four units cost full price and the fifth is
/// discounted — is a different model and deliberately not this one.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class QuantityDiscountTier
{
    /// <summary>The fewest units that select this band.</summary>
    public long MinimumQuantity { get; set; }

    /// <summary>The most that select it. Null only on the final, open-ended band.</summary>
    public long? MaximumQuantity { get; set; }

    /// <summary>
    /// Basis points off the quantity charge, out of 10,000 — the same idiom
    /// <see cref="DiscountTerms.PercentBasisPoints"/> and
    /// <see cref="Price.TaxRateBasisPoints"/> use. 500 is 5%.
    /// </summary>
    /// <remarks>
    /// Basis points rather than a percentage so a third off is exact rather than 33.33 rounded
    /// somewhere unpredictable.
    /// </remarks>
    public int DiscountBasisPoints { get; set; }
}
