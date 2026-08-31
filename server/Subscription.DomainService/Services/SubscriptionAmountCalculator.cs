using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

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
    public static long PeriodAmountMinor(SubscriptionDetail subscription) =>
        FirstPeriodAmountMinor(subscription, default, DateTime.UtcNow);

    /// <summary>
    /// What checkout actually charges when a subscription is first created.
    /// </summary>
    /// <remarks>
    /// A trial is charged nothing, whether or not it asked for a card. Requiring a card and
    /// charging for the first period were the same act for as long as the only way to hold a card
    /// was to take money with it; they are separate now, and a trial that bills on its first day
    /// is not a trial. The card is still collected — see <see cref="RequiresCardSetup"/>, which
    /// this falls through to — it is just collected without a charge.
    /// <para>
    /// Every other subscription reads its own frozen figure, falling back only for one written
    /// before that was frozen at signup.
    /// </para>
    /// <para>
    /// Shared between the checkout charge and the purchase preview so the two read one expression
    /// rather than risk computing a different figure from the same subscription.
    /// </para>
    /// </remarks>
    public static long InitialChargeAmountMinor(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (subscription.Trial is not null)
        {
            return 0;
        }

        return subscription.InitialChargeAmountMinor ?? PeriodAmountMinor(subscription);
    }

    /// <summary>
    /// Whether a card must be on file before this subscription grants anything, given that
    /// nothing is payable today.
    /// </summary>
    /// <remarks>
    /// Two separate reasons, either sufficient. The plan may require a card outright — a fully
    /// discounted first period should not mean an unbillable second one. Or the subscription may
    /// start on a trial the plan said needs a card, which until now could only be honoured by
    /// charging for the first period at signup.
    /// <para>
    /// Shared between checkout and the purchase preview, so a quote cannot promise "no card
    /// needed" for a subscription checkout would then refuse to start without one.
    /// </para>
    /// </remarks>
    public static bool RequiresCardSetup(SubscriptionDetail subscription) =>
        subscription.Plan.RequirePaymentMethodUpfront ||
        subscription.Trial is { RequiresPaymentMethod: true } ||
        subscription.Discount is { Campaign.RequiresPaymentMethodUpfront: true };

    /// <summary>
    /// What the opening period costs, when that period may be a fraction of a month.
    /// </summary>
    /// <remarks>
    /// The same path as a renewal, with one extra step at the front: the gross is scaled to the
    /// dates actually covered before anything else touches it. Everything downstream — the volume
    /// band, the promotional code, the tax — then applies to a prorated gross, which is what makes
    /// the fraction a property of the *period* rather than a discount competing with the others.
    /// <para>
    /// A whole <paramref name="fraction"/> — the default — reproduces the previous arithmetic
    /// exactly, so an anniversary signup is priced by the identical integer operations it always
    /// was.
    /// </para>
    /// </remarks>
    public static long FirstPeriodAmountMinor(
        SubscriptionDetail subscription,
        BillingDayFraction fraction,
        DateTime nowUtc) =>
        FirstPeriodCharge(subscription, fraction, nowUtc).AmountMinor;

    /// <summary>
    /// The opening period's charge in full, including whether a promotion reduced it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FirstPeriodAmountMinor"/> because activation needs the flag and
    /// checkout needs the number, and re-deriving one from the other would be two answers to the
    /// same question.
    /// </remarks>
    /// <param name="includePromotionalDiscount">
    /// False to price without the subscriber's code. Used for an ordinary promotion on the opening
    /// stub of a calendar-aligned yearly price. First-annual campaigns explicitly opt into both the
    /// stub and annual term, with the stub excluded from period consumption.
    /// </param>
    public static PeriodCharge FirstPeriodCharge(
        SubscriptionDetail subscription,
        BillingDayFraction fraction,
        DateTime nowUtc,
        bool includePromotionalDiscount = true)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var (price, quantities) = PricingBasis(subscription, fraction);

        var discounted = DiscountedAmountMinor(
            subscription.Plan,
            includePromotionalDiscount ? subscription.Discount : null,
            price,
            quantities,
            0,
            nowUtc,
            fraction);

        var breakdown = TaxBreakdownFor(
            discounted.AmountMinor,
            price.TaxRateBasisPoints,
            price.TaxMode);

        return discounted with
        {
            AmountMinor = breakdown.TotalAmountMinor,
            NetAmountMinor = breakdown.NetAmountMinor,
            TaxAmountMinor = breakdown.TaxAmountMinor
        };
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
    /// <param name="includePromotionalDiscount">
    /// False to price without the subscriber's code — the opening stub of a calendar-aligned yearly
    /// price, where the code belongs to the year that follows rather than to the days before it.
    /// </param>
    public static PeriodCharge PeriodAmountMinor(
        SubscriptionDetail subscription,
        DateTime nowUtc,
        BillingDayFraction fraction = default,
        bool includePromotionalDiscount = true)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var (price, quantities) = PricingBasis(subscription, fraction);

        var discounted = DiscountedAmountMinor(
            subscription.Plan,
            includePromotionalDiscount ? subscription.Discount : null,
            price,
            quantities,
            subscription.DiscountPeriodsApplied,
            nowUtc,
            fraction);

        var breakdown = TaxBreakdownFor(
            discounted.AmountMinor,
            price.TaxRateBasisPoints,
            price.TaxMode);

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
        DateTime nowUtc,
        BillingDayFraction fraction = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(price);

        // Scaled once, at the front, so every reduction below — the price's own automatic discount,
        // the volume band, the promotional code, and the tax after them — reduces what this period
        // actually costs rather than a month the subscriber is not buying.
        var gross = fraction.Apply(GrossAmountMinor(price, quantityItems));
        var builtIn = BuiltInDiscountCalculator.Resolve(
            gross,
            ProrateBand(
                QuantityDiscountCalculator.ResolveFrom(plan, price, quantityItems),
                gross,
                fraction),
            price.AutomaticDiscountBasisPoints,
            price.QuantityDiscountCombination);

        // A campaign's own precedence governs how it meets the price's automatic and volume
        // discounts, and does so ahead of the plan's QuantityDiscountCombinationPolicy below --
        // deliberately, because that policy exists to settle an ordinary typed-in coupon against
        // the plan's own reductions, and a campaign is a platform-admin decision layered on top of
        // whatever policy the plan happens to have. Left to the plan's policy, a plan authored as
        // QuantityOnly ("built-in discounts only, no code ever counts") would silently discard
        // every campaign ever run against it, which is not a plan author's decision to make about
        // a campaign they may not even know exists.
        if (discount?.Campaign is { } campaign &&
            (campaign.Kind != CampaignKind.Standard || campaign.PrecedenceConfigured))
        {
            switch (campaign.Precedence)
            {
                case CampaignPrecedence.ReplaceBuiltIn:
                {
                    // The automatic discount and the volume band are suppressed entirely -- the
                    // campaign is applied to the raw gross, as if the price had neither. Not "the
                    // larger of the two": SAV/TREX's own rule is that a 15% campaign replaces an
                    // 8% automatic discount outright, even though 15 is already larger and would
                    // have won a BestDiscount comparison anyway -- the distinction matters once a
                    // campaign's rate is smaller than the automatic one it is still meant to
                    // replace.
                    var replaced = ApplyDiscount(gross, discount, periodsApplied, nowUtc, fraction);

                    return replaced with
                    {
                        GrossAmountMinor = gross,
                        BuiltInDiscountMinor = 0,
                        PromotionalDiscountMinor = gross - replaced.AmountMinor
                    };
                }

                case CampaignPrecedence.Stack:
                {
                    // Both, sequentially -- the campaign applied on top of what the automatic
                    // discount and volume band already took off. The same sequencing the plan's
                    // own Stack policy already uses for an ordinary coupon, reused rather than
                    // reinvented: two ways to combine two reductions would eventually disagree
                    // with each other on the same numbers.
                    var stackedCampaign = ApplyDiscount(
                        builtIn.SubtotalMinor, discount, periodsApplied, nowUtc, fraction);

                    return stackedCampaign with
                    {
                        GrossAmountMinor = gross,
                        BuiltInDiscountMinor = builtIn.DiscountAmountMinor,
                        PromotionalDiscountMinor = builtIn.SubtotalMinor - stackedCampaign.AmountMinor
                    };
                }

                default:
                {
                    // BestDiscount: identical to the plan-policy default case below, and
                    // deliberately so -- a campaign that does not ask to replace or stack is asking
                    // for the same conservative "whichever is larger, never both" this codebase
                    // already gives an ordinary coupon.
                    var bestCampaign = ApplyDiscount(gross, discount, periodsApplied, nowUtc, fraction);
                    var bestCampaignDiscount = gross - bestCampaign.AmountMinor;

                    return builtIn.DiscountAmountMinor > bestCampaignDiscount
                        ? new PeriodCharge(builtIn.SubtotalMinor, false, GrossAmountMinor: gross,
                            BuiltInDiscountMinor: builtIn.DiscountAmountMinor)
                        : bestCampaign with
                        {
                            GrossAmountMinor = gross,
                            PromotionalDiscountMinor = bestCampaignDiscount
                        };
                }
            }
        }

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
                    builtIn.SubtotalMinor, discount, periodsApplied, nowUtc, fraction);

                return stacked with
                {
                    GrossAmountMinor = gross,
                    BuiltInDiscountMinor = builtIn.DiscountAmountMinor,
                    PromotionalDiscountMinor = builtIn.SubtotalMinor - stacked.AmountMinor
                };
            }

            default:
            {
                var promotional = ApplyDiscount(gross, discount, periodsApplied, nowUtc, fraction);
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
    /// Which price and quantities a period is charged from.
    /// </summary>
    /// <remarks>
    /// Normally the subscription's own, and for every monthly subscription always its own. The
    /// exception is a partial period on a calendar-aligned <em>yearly</em> price: a week of an
    /// annual amount is not a meaningful quantity, so the stub is priced from the monthly price
    /// that annual price was linked to and snapshotted from.
    /// <para>
    /// A whole period is never re-based. Once the annual cycle opens on the first, the subscriber
    /// is buying a year and is charged the annual amount.
    /// </para>
    /// </remarks>
    private static (PriceSnapshot Price, IReadOnlyList<SubscriptionQuantityItem> Quantities)
        PricingBasis(SubscriptionDetail subscription, BillingDayFraction fraction) =>
        fraction.IsPartial &&
        CalendarBillingAlignment.TryStubBasis(
            subscription.Price,
            subscription.QuantityItems,
            out var stubPrice,
            out var stubQuantities)
            ? (stubPrice, stubQuantities)
            : (subscription.Price, subscription.QuantityItems);

    /// <summary>
    /// A volume band re-expressed against a prorated gross.
    /// </summary>
    /// <remarks>
    /// <see cref="BuiltInDiscountCalculator"/> uses a band's resolved <em>money</em> verbatim when
    /// the band wins, deliberately, so a plan that priced its bands one way before automatic
    /// discounts existed keeps doing so to the minor unit. That figure is a whole month's, though —
    /// handing it to a stub period would take a full month's volume discount off a fraction of a
    /// month's charge, and a large enough band would make the stub free.
    /// <para>
    /// So the band's <em>rate</em> is re-applied to the prorated gross, truncated exactly as
    /// <see cref="QuantityDiscountCalculator"/> and <see cref="BuiltInDiscountCalculator"/> both
    /// truncate. A whole period returns the band untouched, which is every anniversary
    /// subscription and every renewal after an opening stub.
    /// </para>
    /// </remarks>
    private static QuantityDiscountOutcome ProrateBand(
        QuantityDiscountOutcome band,
        long proratedGrossMinor,
        BillingDayFraction fraction)
    {
        if (!fraction.IsPartial || band.DiscountBasisPoints <= 0)
        {
            return band;
        }

        var discount = proratedGrossMinor * band.DiscountBasisPoints / 10_000;

        return band with
        {
            GrossAmountMinor = proratedGrossMinor,
            DiscountAmountMinor = discount,
            SubtotalMinor = Math.Max(0, proratedGrossMinor - discount)
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
        DateTime nowUtc,
        BillingDayFraction fraction = default)
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
            // A percentage needs no scaling — it is already a percentage of an amount that was
            // scaled. A fixed sum does: "10 off" against a week of a month would otherwise take a
            // whole month's discount off a quarter of a month's charge, and a large enough one
            // would make a stub period free.
            DiscountKind.FixedAmount when discount.AmountMinor is { } off =>
                amountMinor - fraction.Apply(off),
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
        //
        // Checked throughout, including the final addition: a gross amount that comfortably fits
        // a long on its own can still overflow once exclusive tax is added on top, and an
        // unchecked addition here would silently wrap the total (possibly negative) instead of
        // refusing it — see SubscriptionUsageRater.WalkTierRange's own remarks for why this
        // module never lets that happen quietly. OverflowException propagates to the caller,
        // which turns it into a refusal (the preview) or a deferral (period-end rating) rather
        // than ever persisting a wrapped amount.
        checked
        {
            var exclusiveTax = taxMode is null
                ? (long)((Int128)discountedAmountMinor * basisPoints / 10_000)
                : RoundedQuotient(discountedAmountMinor, basisPoints, 10_000);

            return new TaxBreakdown(
                discountedAmountMinor,
                exclusiveTax,
                discountedAmountMinor + exclusiveTax);
        }
    }

    /// <summary>
    /// <c>amount × numerator / denominator</c>, rounded to the nearest whole minor unit.
    /// </summary>
    /// <remarks>
    /// Exact integer arithmetic, widened for the multiplication so a large amount times a rate
    /// cannot overflow, and never a floating-point ratio — the rest of this module's money
    /// deliberately never touches one. The narrowing cast back down to <see cref="long"/> is
    /// checked too: <c>checked</c>/<c>unchecked</c> context does not cross a method call, so this
    /// has to guard its own cast rather than rely on a caller's <c>checked</c> block doing it.
    /// </remarks>
    private static long RoundedQuotient(long amountMinor, long numerator, long denominator) =>
        checked((long)(((Int128)amountMinor * numerator + denominator / 2) / denominator));
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
