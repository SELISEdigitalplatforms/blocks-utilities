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
    /// <summary>Kept for the first charge, where no prior period and no discount history exist yet.</summary>
    public static long PeriodAmountMinor(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var gross = GrossAmountMinor(subscription.Price, subscription.QuantityItems);

        return ApplyDiscount(gross, subscription.Discount, 0, DateTime.UtcNow).AmountMinor;
    }

    /// <summary>
    /// What a renewal charges, and whether the discount actually reduced it — so the caller
    /// knows whether to count this period against <see cref="DiscountTerms.DurationPeriods"/>.
    /// Any banked <see cref="SubscriptionDetail.CreditBalanceMinor"/> is applied after the
    /// discount, and never below zero.
    /// </summary>
    public static PeriodCharge PeriodAmountMinor(SubscriptionDetail subscription, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var gross = GrossAmountMinor(subscription.Price, subscription.QuantityItems);
        var discounted = ApplyDiscount(
            gross,
            subscription.Discount,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        var creditConsumed = Math.Min(
            Math.Max(0, subscription.CreditBalanceMinor),
            discounted.AmountMinor);

        return discounted with
        {
            AmountMinor = discounted.AmountMinor - creditConsumed,
            CreditConsumedMinor = creditConsumed
        };
    }

    /// <summary>
    /// The undiscounted cost of a price and quantity pair — exposed so proration can price a
    /// *different* plan the same way a renewal prices the current one, without duplicating this
    /// logic.
    /// </summary>
    internal static long GrossAmountMinor(
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantityItems);

        if (string.IsNullOrWhiteSpace(price.QuantityItemKey))
        {
            return price.UnitAmountMinor;
        }

        var matching = quantityItems
            .Where(item => string.Equals(
                item.ItemKey,
                price.QuantityItemKey,
                StringComparison.Ordinal))
            .ToList();

        var quantity = matching.Sum(item => item.Quantity);

        // The amount snapshotted on the item, not the plan's current price: adding units later
        // charges what the subscriber agreed to.
        var unitAmount = matching
            .Select(item => item.UnitAmountMinor)
            .DefaultIfEmpty(price.UnitAmountMinor)
            .First();

        return quantity * unitAmount;
    }

    /// <summary>
    /// Applies a discount to a gross amount — exposed so proration can discount a hypothetical
    /// target plan's cost exactly as a renewal discounts the current one.
    /// </summary>
    internal static PeriodCharge ApplyDiscount(
        long amountMinor,
        DiscountTerms? discount,
        int periodsApplied,
        DateTime nowUtc)
    {
        if (discount is null ||
            amountMinor <= 0 ||
            !DiscountStillActive(discount, periodsApplied, nowUtc))
        {
            return new PeriodCharge(amountMinor, false);
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
        return new PeriodCharge(Math.Max(0, discounted), true);
    }

    /// <summary>
    /// Whether a discount still covers the period being charged. A duration expires on the
    /// count of periods it has already reduced, an expiry date on the wall clock — either can
    /// end it independently of the other.
    /// </summary>
    private static bool DiscountStillActive(
        DiscountTerms discount,
        int periodsApplied,
        DateTime nowUtc) =>
        (discount.DurationPeriods is not { } maxPeriods || periodsApplied < maxPeriods) &&
        (discount.ExpiresAtUtc is not { } expiresAtUtc || nowUtc < expiresAtUtc);
}

/// <summary>What a period costs, whether a discount reduced it, and how much banked credit paid for it.</summary>
public readonly record struct PeriodCharge(
    long AmountMinor,
    bool DiscountApplied,
    long CreditConsumedMinor = 0);
