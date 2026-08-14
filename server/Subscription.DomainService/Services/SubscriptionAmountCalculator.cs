using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a subscription's period costs, in minor units.
/// </summary>
/// <remarks>
/// Pure and static so it can be exercised directly. Money arithmetic stays in
/// <see cref="long"/> throughout: a decimal only appears at the boundary where the payment
/// module needs one, and never in a calculation.
/// </remarks>
public static class SubscriptionAmountCalculator
{
    public static long PeriodAmountMinor(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var gross = GrossAmountMinor(subscription);

        return ApplyDiscount(gross, subscription.Discount);
    }

    private static long GrossAmountMinor(SubscriptionDetail subscription)
    {
        var price = subscription.Price;

        if (string.IsNullOrWhiteSpace(price.QuantityItemKey))
        {
            return price.UnitAmountMinor;
        }

        var quantity = subscription.QuantityItems
            .Where(item => string.Equals(
                item.ItemKey,
                price.QuantityItemKey,
                StringComparison.Ordinal))
            .Sum(item => item.Quantity);

        // The amount snapshotted on the item, not the plan's current price: adding units later
        // charges what the subscriber agreed to.
        var unitAmount = subscription.QuantityItems
            .Where(item => string.Equals(
                item.ItemKey,
                price.QuantityItemKey,
                StringComparison.Ordinal))
            .Select(item => item.UnitAmountMinor)
            .DefaultIfEmpty(price.UnitAmountMinor)
            .First();

        return quantity * unitAmount;
    }

    private static long ApplyDiscount(long amountMinor, DiscountTerms? discount)
    {
        if (discount is null || amountMinor <= 0)
        {
            return amountMinor;
        }

        var discounted = discount.Kind switch
        {
            DiscountKind.Percent when discount.PercentBasisPoints is { } basisPoints =>
                amountMinor - (amountMinor * basisPoints / 10_000),
            DiscountKind.FixedAmount when discount.AmountMinor is { } off =>
                amountMinor - off,
            _ => amountMinor
        };

        // A discount can take a charge to nothing but never below it: a negative charge is a
        // refund, and one must never arrive by arithmetic.
        return Math.Max(0, discounted);
    }
}
