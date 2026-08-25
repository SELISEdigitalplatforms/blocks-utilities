using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// How a price's automatic discount is named to a caller.
/// </summary>
/// <remarks>
/// One place, for the reason <see cref="SubscriptionTaxPresentation"/> is one place: the rule has an
/// edge that every response carrying it would otherwise get slightly differently. A price with a
/// discount and no stored combination is calculated as <see cref="AutomaticDiscountCombination.BestDiscount"/>,
/// so that is what is reported; a price with no automatic discount reports nothing at all, since
/// naming a combination for a discount that does not exist invites a client to explain a reduction
/// of zero.
/// </remarks>
public static class SubscriptionDiscountPresentation
{
    public static string? Describe(
        int? automaticDiscountBasisPoints,
        AutomaticDiscountCombination? combination) =>
        automaticDiscountBasisPoints > 0
            ? (combination ?? AutomaticDiscountCombination.BestDiscount).ToString()
            : null;

    public static string? Describe(PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return Describe(price.AutomaticDiscountBasisPoints, price.QuantityDiscountCombination);
    }

    /// <summary>
    /// The rate itself, reported only when there is one — so a client is never handed a discount of
    /// zero basis points to render.
    /// </summary>
    public static int? RateOf(PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return price.AutomaticDiscountBasisPoints > 0
            ? price.AutomaticDiscountBasisPoints
            : null;
    }
}
