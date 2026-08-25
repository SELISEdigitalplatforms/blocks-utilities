using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Whether a discount may be used against a given plan and price.
/// </summary>
/// <remarks>
/// One place, because the question is asked twice about the same discount at two different moments —
/// when a code is redeemed at signup, and again when a subscriber moves to a different price. Two
/// implementations of "does this code apply here" is how a monthly-only coupon comes to be honoured
/// on an annual price: the second caller simply never asks.
/// <para>
/// The two lists <em>narrow</em>. Empty is unrestricted by that dimension, which is what every
/// discount authored before either restriction existed carries; populated, it must match. When both
/// are populated both must match, so naming a plan and a price is a narrower offer rather than two
/// ways to qualify.
/// </para>
/// </remarks>
public static class SubscriptionDiscountApplicability
{
    public static bool Permits(
        IReadOnlyCollection<string> applicablePlanCodes,
        IReadOnlyCollection<string> applicablePriceIds,
        string planCode,
        string priceId)
    {
        ArgumentNullException.ThrowIfNull(applicablePlanCodes);
        ArgumentNullException.ThrowIfNull(applicablePriceIds);

        return (applicablePlanCodes.Count == 0 ||
                applicablePlanCodes.Contains(planCode, StringComparer.Ordinal)) &&
               (applicablePriceIds.Count == 0 ||
                applicablePriceIds.Contains(priceId, StringComparer.Ordinal));
    }

    /// <summary>Against the catalogue entry, as a code is being redeemed.</summary>
    public static bool Permits(Discount discount, string planCode, string priceId)
    {
        ArgumentNullException.ThrowIfNull(discount);

        return Permits(
            discount.ApplicablePlanCodes,
            discount.ApplicablePriceIds,
            planCode,
            priceId);
    }

    /// <summary>
    /// Against the terms copied onto a subscription, as it is being moved somewhere else.
    /// </summary>
    /// <remarks>
    /// Reads the snapshot, never the catalogue: a discount retired or re-scoped since the subscriber
    /// redeemed it must be judged by the offer they actually accepted.
    /// </remarks>
    public static bool Permits(DiscountTerms terms, string planCode, string priceId)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return Permits(
            terms.ApplicablePlanCodes,
            terms.ApplicablePriceIds,
            planCode,
            priceId);
    }
}
