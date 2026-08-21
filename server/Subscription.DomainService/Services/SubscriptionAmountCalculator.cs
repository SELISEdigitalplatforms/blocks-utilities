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

        var discounted = DiscountedAmountMinor(
            subscription.Plan,
            subscription.Discount,
            subscription.Price,
            subscription.QuantityItems,
            0,
            DateTime.UtcNow).AmountMinor;

        return discounted + TaxAmountMinor(discounted, subscription.Price.TaxRateBasisPoints);
    }

    /// <summary>
    /// What a renewal charges, and whether the discount actually reduced it — so the caller
    /// knows whether to count this period against <see cref="DiscountTerms.DurationPeriods"/>.
    /// Tax is added to the discounted amount, and any banked
    /// <see cref="SubscriptionDetail.CreditBalanceMinor"/> is then consumed against that
    /// tax-inclusive total, never below zero — a credit offsets what the subscriber owes
    /// including tax, it does not shrink the taxable base.
    /// </summary>
    public static PeriodCharge PeriodAmountMinor(SubscriptionDetail subscription, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var discounted = DiscountedAmountMinor(
            subscription.Plan,
            subscription.Discount,
            subscription.Price,
            subscription.QuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        var tax = TaxAmountMinor(discounted.AmountMinor, subscription.Price.TaxRateBasisPoints);
        var taxInclusive = discounted.AmountMinor + tax;

        var creditConsumed = Math.Min(
            Math.Max(0, subscription.CreditBalanceMinor),
            taxInclusive);

        return discounted with
        {
            AmountMinor = taxInclusive - creditConsumed,
            TaxAmountMinor = tax,
            CreditConsumedMinor = creditConsumed
        };
    }

    /// <summary>
    /// A period's cost after both reductions a subscription can hold: its quantity's volume band
    /// and its promotional code, combined the way its plan says to.
    /// </summary>
    /// <remarks>
    /// Every money path goes through here so a band cannot be applied twice, or forgotten once.
    /// Exposed to proration for the same reason <see cref="ApplyDiscount"/> is: a plan change has
    /// to price a hypothetical target exactly as a renewal prices the current subscription.
    /// <para>
    /// <see cref="PeriodCharge.DiscountApplied"/> reports the <em>promotion</em> only, never the
    /// band. It exists to count periods against
    /// <see cref="DiscountTerms.DurationPeriods"/>, and a promotion that lost to a volume band has
    /// reduced nothing — spending a customer's three months of "20% off" on periods where the band
    /// was larger would expire it without them ever seeing it.
    /// </para>
    /// </remarks>
    internal static PeriodCharge DiscountedAmountMinor(
        PlanSnapshot plan,
        DiscountTerms? discount,
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems,
        int periodsApplied,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(price);

        var gross = GrossAmountMinor(price, quantityItems);
        var band = QuantityDiscountCalculator.ResolveFrom(plan, price, quantityItems);
        var bandDiscount = band.DiscountAmountMinor;

        switch (plan.QuantityDiscountCombinationPolicy)
        {
            case QuantityDiscountCombinationPolicy.QuantityOnly:
                return new PeriodCharge(Math.Max(0, gross - bandDiscount), false);

            case QuantityDiscountCombinationPolicy.Stack:
            {
                var afterBand = Math.Max(0, gross - bandDiscount);
                return ApplyDiscount(afterBand, discount, periodsApplied, nowUtc);
            }

            default:
            {
                var promotional = ApplyDiscount(gross, discount, periodsApplied, nowUtc);
                var promotionalDiscount = gross - promotional.AmountMinor;

                // Ties go to the promotion, so a band worth the same as a code does not silently
                // stop the code being consumed.
                return bandDiscount > promotionalDiscount
                    ? new PeriodCharge(Math.Max(0, gross - bandDiscount), false)
                    : promotional;
            }
        }
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

    /// <summary>
    /// Tax on an already-discounted amount — exposed so proration can tax each side of a plan
    /// change at that side's own price's rate.
    /// </summary>
    internal static long TaxAmountMinor(long discountedAmountMinor, int? taxRateBasisPoints) =>
        taxRateBasisPoints is { } basisPoints && discountedAmountMinor > 0
            ? discountedAmountMinor * basisPoints / 10_000
            : 0;
}

/// <summary>
/// What a period costs, whether a discount reduced it, how much of the total is tax, and how
/// much banked credit paid for it.
/// </summary>
public readonly record struct PeriodCharge(
    long AmountMinor,
    bool DiscountApplied,
    long CreditConsumedMinor = 0,
    long TaxAmountMinor = 0);
