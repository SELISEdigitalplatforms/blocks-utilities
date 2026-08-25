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

        return TaxBreakdownFor(
            discounted,
            subscription.Price.TaxRateBasisPoints,
            subscription.Price.TaxMode).TotalAmountMinor;
    }

    /// <summary>
    /// What a renewal charges, and whether the discount actually reduced it — so the caller
    /// knows whether to count this period against <see cref="DiscountTerms.DurationPeriods"/>.
    /// The discounted amount is split into net and tax by the price's own mode — added on top when
    /// the price is exclusive, extracted from it when inclusive — and any banked
    /// <see cref="SubscriptionDetail.CreditBalanceMinor"/> is then consumed against the resulting
    /// total, never below zero. A credit offsets what the subscriber owes including tax; it does not
    /// shrink the taxable base, which is why it is applied last.
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

        var breakdown = TaxBreakdownFor(
            discounted.AmountMinor,
            subscription.Price.TaxRateBasisPoints,
            subscription.Price.TaxMode);

        var creditConsumed = Math.Min(
            Math.Max(0, subscription.CreditBalanceMinor),
            breakdown.TotalAmountMinor);

        return discounted with
        {
            AmountMinor = breakdown.TotalAmountMinor - creditConsumed,
            NetAmountMinor = breakdown.NetAmountMinor,
            TaxAmountMinor = breakdown.TaxAmountMinor,
            CreditConsumedMinor = creditConsumed
        };
    }

    /// <summary>
    /// A period's cost after every reduction a subscription can hold: the price's own automatic
    /// discount, its quantity's volume band, and its promotional code — the first two combined the
    /// way the price says to, and the result combined with the code the way the plan says to.
    /// </summary>
    /// <remarks>
    /// Every money path goes through here so a reduction cannot be applied twice, or forgotten once.
    /// Exposed to proration for the same reason <see cref="ApplyDiscount"/> is: a plan change has
    /// to price a hypothetical target exactly as a renewal prices the current subscription.
    /// <para>
    /// Two combinations, deliberately, because they answer different questions.
    /// <see cref="BuiltInDiscountCalculator"/> settles what a subscriber gets without asking — a
    /// cadence discount and a volume band, both authored by the merchant — and the plan's
    /// <see cref="QuantityDiscountCombinationPolicy"/> then settles what a code they typed adds to
    /// it. Collapsing the two would make "8% for paying yearly" negotiate with a coupon.
    /// </para>
    /// <para>
    /// <see cref="PeriodCharge.DiscountApplied"/> reports the <em>promotion</em> only, never the
    /// built-in reduction. It exists to count periods against
    /// <see cref="DiscountTerms.DurationPeriods"/>, and a promotion that lost to a volume band or a
    /// cadence discount has reduced nothing — spending a customer's three months of "20% off" on
    /// periods where the built-in discount was larger would expire it without them ever seeing it.
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
        var builtIn = BuiltInDiscountCalculator.Resolve(
            gross,
            QuantityDiscountCalculator.ResolveFrom(plan, price, quantityItems),
            price.AutomaticDiscountBasisPoints,
            price.QuantityDiscountCombination);

        switch (plan.QuantityDiscountCombinationPolicy)
        {
            case QuantityDiscountCombinationPolicy.QuantityOnly:
                // "Built-in discounts only" — the stored name predates there being more than one of
                // them, and the wire value is kept so an existing plan means what it always did.
                return new PeriodCharge(builtIn.SubtotalMinor, false, GrossAmountMinor: gross,
                    BuiltInDiscountMinor: builtIn.DiscountAmountMinor);

            case QuantityDiscountCombinationPolicy.Stack:
            {
                var stacked = ApplyDiscount(
                    builtIn.SubtotalMinor, discount, periodsApplied, nowUtc);

                return stacked with
                {
                    GrossAmountMinor = gross,
                    BuiltInDiscountMinor = builtIn.DiscountAmountMinor,
                    PromotionalDiscountMinor = builtIn.SubtotalMinor - stacked.AmountMinor
                };
            }

            default:
            {
                var promotional = ApplyDiscount(gross, discount, periodsApplied, nowUtc);
                var promotionalDiscount = gross - promotional.AmountMinor;

                // Ties go to the promotion, so a built-in reduction worth the same as a code does
                // not silently stop the code being consumed.
                return builtIn.DiscountAmountMinor > promotionalDiscount
                    ? new PeriodCharge(builtIn.SubtotalMinor, false, GrossAmountMinor: gross,
                        BuiltInDiscountMinor: builtIn.DiscountAmountMinor)
                    : promotional with
                    {
                        GrossAmountMinor = gross,
                        PromotionalDiscountMinor = promotionalDiscount
                    };
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
    internal static bool DiscountStillActive(
        DiscountTerms discount,
        int periodsApplied,
        DateTime nowUtc) =>
        (discount.DurationPeriods is not { } maxPeriods || periodsApplied < maxPeriods) &&
        (discount.ExpiresAtUtc is not { } expiresAtUtc || nowUtc < expiresAtUtc);

    /// <summary>
    /// Splits a discounted amount into net, tax and total — exposed so proration can settle each
    /// side of a plan change at that side's own price's rate and mode.
    /// </summary>
    /// <remarks>
    /// <paramref name="discountedAmountMinor"/> is the <em>configured</em> amount after discounts,
    /// which is a different thing in each mode: exclusive, it is the net and tax is added to it;
    /// inclusive, it is the total and the tax is already inside it. That is the whole difference
    /// between the two, and it lives here rather than at each of the five call sites.
    /// <para>
    /// Applied once to the aggregate. Taxing each line and summing gives a different answer for the
    /// same charge — three lines each losing half a cent to rounding is a cent and a half the
    /// invoice cannot explain.
    /// </para>
    /// <para>
    /// Rounded to the nearest minor unit, halves away from zero: 7.7% of CHF 145.00 is 1116.5 cents
    /// and the tax is CHF 11.17. Integer arithmetic throughout, widened to <see cref="Int128"/> for
    /// the multiplication — the same reason proration does, since an amount times a basis-point rate
    /// overflows a <see cref="long"/> well before the amounts involved look unreasonable.
    /// </para>
    /// <para>
    /// This is the one place where a tax-exclusive charge can differ from what this module produced
    /// before modes existed, and only ever by a single minor unit on a rate that lands exactly on a
    /// half. Rounding is what the two modes need in common: truncating an inclusive split would hand
    /// the merchant the fraction on every invoice, and having the two modes round differently would
    /// be worse than either.
    /// </para>
    /// </remarks>
    internal static TaxBreakdown TaxBreakdownFor(
        long discountedAmountMinor,
        int? taxRateBasisPoints,
        TaxMode? taxMode)
    {
        if (taxRateBasisPoints is not { } basisPoints ||
            basisPoints <= 0 ||
            discountedAmountMinor <= 0)
        {
            // No rate configured, or nothing to tax. Either way the configured amount is the whole
            // charge, and it is neither net-of-something nor inclusive-of-anything.
            return new TaxBreakdown(discountedAmountMinor, 0, discountedAmountMinor);
        }

        // A rate with no mode is a price authored before modes existed, and every one of those was
        // charged exclusively. Reading it as inclusive would quietly reduce what an existing
        // subscription is worth to the merchant.
        if ((taxMode ?? TaxMode.Exclusive) == TaxMode.Inclusive)
        {
            // The tax already inside the amount: rate over rate-plus-one-hundred-percent.
            var tax = RoundedQuotient(discountedAmountMinor, basisPoints, 10_000 + basisPoints);

            return new TaxBreakdown(
                discountedAmountMinor - tax,
                tax,
                discountedAmountMinor);
        }

        // A null mode marks a price/snapshot authored before modes existed. Preserve the exact
        // legacy calculation (integer truncation) so an existing renewal is never repriced by a
        // catalogue-presentation feature. Explicitly authored modes use the documented half-up
        // rule shared with inclusive prices.
        var exclusiveTax = taxMode is null
            ? (long)((Int128)discountedAmountMinor * basisPoints / 10_000)
            : RoundedQuotient(discountedAmountMinor, basisPoints, 10_000);

        return new TaxBreakdown(
            discountedAmountMinor,
            exclusiveTax,
            discountedAmountMinor + exclusiveTax);
    }

    /// <summary>
    /// <c>amount × numerator / denominator</c>, rounded to the nearest whole minor unit.
    /// </summary>
    /// <remarks>
    /// Exact integer arithmetic, widened for the multiplication so a large amount times a rate
    /// cannot overflow, and never a floating-point ratio — the rest of this module's money
    /// deliberately never touches one.
    /// </remarks>
    private static long RoundedQuotient(long amountMinor, long numerator, long denominator) =>
        (long)(((Int128)amountMinor * numerator + denominator / 2) / denominator);
}

/// <summary>
/// One charge, split three ways. <see cref="TotalAmountMinor"/> is always
/// <see cref="NetAmountMinor"/> plus <see cref="TaxAmountMinor"/> — by construction, not by a
/// second calculation, so an invoice's lines can never fail to add up to what was charged.
/// </summary>
public readonly record struct TaxBreakdown(
    long NetAmountMinor,
    long TaxAmountMinor,
    long TotalAmountMinor);

/// <summary>
/// What a period costs, whether a discount reduced it, how it splits into net and tax, and how
/// much banked credit paid for it.
/// </summary>
/// <remarks>
/// <see cref="AmountMinor"/> is what the payer is charged, so it is the total <em>after</em> credit.
/// <see cref="NetAmountMinor"/> and <see cref="TaxAmountMinor"/> describe the charge before any
/// credit was spent against it, because that is the split an invoice has to show: a credit pays a
/// bill, it does not change what the bill was for.
/// <para>
/// <see cref="GrossAmountMinor"/>, <see cref="BuiltInDiscountMinor"/> and
/// <see cref="PromotionalDiscountMinor"/> are what the charge is made of, so a subscriber can be
/// told why they are paying what they are paying: gross, less the two kinds of reduction, is the
/// amount that was then taxed. Reported rather than recomputed, because recomputing a discount from
/// a total requires knowing which of several combinations produced it.
/// </para>
/// </remarks>
public readonly record struct PeriodCharge(
    long AmountMinor,
    bool DiscountApplied,
    long CreditConsumedMinor = 0,
    long TaxAmountMinor = 0,
    long NetAmountMinor = 0,
    long GrossAmountMinor = 0,
    long BuiltInDiscountMinor = 0,
    long PromotionalDiscountMinor = 0);
