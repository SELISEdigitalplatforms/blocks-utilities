using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The part of a price an operator may still edit after it has been sold: its automatic discount
/// and its tax.
/// </summary>
/// <remarks>
/// Everything else about a price is immutable once it exists. These four are the deliberate
/// exception, and they travel together because a renewal that installed a new discount beside a
/// stale tax — or the reverse — would bill a combination nobody configured.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PriceTerms
{
    public int? AutomaticDiscountBasisPoints { get; set; }

    public AutomaticDiscountCombination? QuantityDiscountCombination { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    /// <summary>The terms a subscription is billed on today.</summary>
    public static PriceTerms Of(PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return new PriceTerms
        {
            AutomaticDiscountBasisPoints = price.AutomaticDiscountBasisPoints,
            QuantityDiscountCombination = price.QuantityDiscountCombination,
            TaxRateBasisPoints = price.TaxRateBasisPoints,
            TaxMode = price.TaxMode
        };
    }

    /// <summary>Bills <paramref name="price"/> on these terms, leaving everything else about it alone.</summary>
    /// <remarks>
    /// In place, the way a renewal already repaints its in-memory copy with a scheduled plan change
    /// before pricing. A field-by-field copy would silently drop whatever is added to
    /// <see cref="PriceSnapshot"/> next.
    /// </remarks>
    public void ApplyTo(PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        price.AutomaticDiscountBasisPoints = AutomaticDiscountBasisPoints;
        price.QuantityDiscountCombination = QuantityDiscountCombination;
        price.TaxRateBasisPoints = TaxRateBasisPoints;
        price.TaxMode = TaxMode;
    }

    /// <summary>Whether these terms would bill exactly as <paramref name="other"/> does.</summary>
    public bool SameAs(PriceTerms other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return AutomaticDiscountBasisPoints == other.AutomaticDiscountBasisPoints &&
            QuantityDiscountCombination == other.QuantityDiscountCombination &&
            TaxRateBasisPoints == other.TaxRateBasisPoints &&
            TaxMode == other.TaxMode;
    }
}
