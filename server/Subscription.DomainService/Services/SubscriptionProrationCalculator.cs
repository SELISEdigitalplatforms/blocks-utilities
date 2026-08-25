using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a mid-period plan change costs, or credits, right now.
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="SubscriptionAmountCalculator"/> — the instant is always a
/// parameter. Both the old and new amounts go through the exact same gross, discount and tax math
/// a renewal uses (<see cref="SubscriptionAmountCalculator.GrossAmountMinor"/>,
/// <see cref="SubscriptionAmountCalculator.ApplyDiscount"/>,
/// <see cref="SubscriptionAmountCalculator.TaxBreakdownFor"/>), just against two different
/// price/quantity pairs. The discount is the subscriber's, not the plan's, so it applies to both
/// sides identically — but tax is each side's own price's rate and mode, since a plan change can
/// move the subscriber to a differently-taxed price, or to one that quotes tax the other way
/// round.
/// </remarks>
public static class SubscriptionProrationCalculator
{
    /// <param name="targetPlan">
    /// The plan being priced on the new side. The current plan for a quantity change, a different
    /// one for a plan change — its volume bands and combination policy are what the new side is
    /// held to, since those are what the subscriber is moving onto.
    /// </param>
    public static ProrationOutcome Calculate(
        SubscriptionDetail subscription,
        PlanSnapshot targetPlan,
        PriceSnapshot targetPrice,
        IReadOnlyList<SubscriptionQuantityItem> targetQuantityItems,
        DateTime nowUtc,
        DateTime targetPeriodStartUtc,
        DateTime targetPeriodEndUtc,
        BillingDayFraction targetFraction = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(targetPlan);
        ArgumentNullException.ThrowIfNull(targetPrice);
        ArgumentNullException.ThrowIfNull(targetQuantityItems);

        var totalTicks = (subscription.CurrentPeriodEndUtc - subscription.CurrentPeriodStartUtc).Ticks;

        if (totalTicks <= 0)
        {
            // A malformed period grants nothing either way rather than dividing by zero or
            // guessing — the caller should not have reached here with one.
            return new ProrationOutcome(0, subscription.CreditBalanceMinor);
        }

        var remainingTicks = Math.Clamp(
            (subscription.CurrentPeriodEndUtc - nowUtc).Ticks,
            0,
            totalTicks);

        // Both sides through the same band-aware path a renewal uses, so a quantity change is
        // priced at the band its quantity actually selects rather than at the flat unit amount.
        var oldDiscounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            subscription.Plan,
            subscription.Discount,
            subscription.Price,
            subscription.QuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        // The target's own fraction, where it has one. A move onto a calendar-aligned price buys
        // the days from here to the first of next month, and those are counted as calendar dates
        // rather than as elapsed time — the same 7/31 a fresh signup on the same day would pay.
        var newDiscounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            targetPlan,
            subscription.Discount,
            targetPrice,
            targetQuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc,
            targetFraction);

        // Each side settled at its own price's rate *and* mode, before the two are netted against
        // each other. A plan change can move a subscriber from a tax-exclusive price to an inclusive
        // one, and netting the configured amounts first would compare a number that contains tax
        // with one that does not.
        var oldTaxInclusive = SubscriptionAmountCalculator.TaxBreakdownFor(
            oldDiscounted.AmountMinor,
            subscription.Price.TaxRateBasisPoints,
            subscription.Price.TaxMode).TotalAmountMinor;
        var newTaxInclusive = SubscriptionAmountCalculator.TaxBreakdownFor(
            newDiscounted.AmountMinor,
            targetPrice.TaxRateBasisPoints,
            targetPrice.TaxMode).TotalAmountMinor;

        var oldRemainingValue = Prorate(oldTaxInclusive, remainingTicks, totalTicks);
        var targetTotalTicks = (targetPeriodEndUtc - targetPeriodStartUtc).Ticks;
        var targetRemainingTicks = Math.Clamp(
            (targetPeriodEndUtc - nowUtc).Ticks,
            0,
            Math.Max(0, targetTotalTicks));

        // A day-counted target is already exactly the period being bought — it runs from now to
        // the next boundary — so scaling it again by the time left in it would prorate it twice.
        var newRemainingCost = targetFraction.IsPartial
            ? newTaxInclusive
            : targetTotalTicks <= 0
                ? 0
                : Prorate(newTaxInclusive, targetRemainingTicks, targetTotalTicks);

        var rawDelta = newRemainingCost - oldRemainingValue;
        var netAfterCredit = rawDelta - subscription.CreditBalanceMinor;

        // Reported, not recomputed later. Every figure below was needed to reach the charge, and an
        // invoice for a settlement cannot be explained from the charge alone: "CHF 41.30" is the
        // remainder of a subtraction between two prorated periods, and a subscriber asking why is
        // asking about the two sides, not the remainder.
        var breakdown = new ProrationBreakdown(
            new ProrationSide(
                oldDiscounted.GrossAmountMinor,
                oldDiscounted.BuiltInDiscountMinor,
                oldDiscounted.PromotionalDiscountMinor,
                oldTaxInclusive - oldDiscounted.AmountMinor,
                oldTaxInclusive,
                oldRemainingValue),
            new ProrationSide(
                newDiscounted.GrossAmountMinor,
                newDiscounted.BuiltInDiscountMinor,
                newDiscounted.PromotionalDiscountMinor,
                newTaxInclusive - newDiscounted.AmountMinor,
                newTaxInclusive,
                newRemainingCost),
            // What the credit balance actually paid for. A downgrade has a negative delta and spends
            // nothing — the credit grows instead, which the outcome below already carries.
            Math.Clamp(rawDelta, 0, Math.Max(0, subscription.CreditBalanceMinor)),
            netAfterCredit);

        return netAfterCredit > 0
            ? new ProrationOutcome(netAfterCredit, 0, breakdown)
            : new ProrationOutcome(0, -netAfterCredit, breakdown);
    }

    /// <summary>
    /// Scales an amount by the fraction of the period remaining, in exact integer arithmetic.
    /// </summary>
    /// <remarks>
    /// <c>amount * remainingTicks</c> overflows a <see cref="long"/> long before the numbers
    /// involved look unreasonable — a year-long period is already ~3×10^14 ticks. Widening to
    /// <see cref="Int128"/> for the multiplication keeps this exact rather than reaching for a
    /// floating-point ratio, which the rest of this module's money arithmetic deliberately never
    /// does.
    /// </remarks>
    private static long Prorate(long amountMinor, long remainingTicks, long totalTicks) =>
        (long)((Int128)amountMinor * remainingTicks / totalTicks);
}

/// <param name="ChargeMinor">What to charge now. Zero when the change is fully covered by credit.</param>
/// <param name="NewCreditBalanceMinor">
/// The credit balance to write back — either fully consumed (zero) or increased by an amount a
/// downgrade could not immediately spend.
/// </param>
/// <param name="Breakdown">
/// The two sides the charge came from, for the payment record. Default when the period was malformed
/// and nothing could be prorated.
/// </param>
public readonly record struct ProrationOutcome(
    long ChargeMinor,
    long NewCreditBalanceMinor,
    ProrationBreakdown Breakdown = default);

/// <summary>
/// One side of a settlement: what a period costs, and how much of that this instant is worth.
/// </summary>
/// <param name="GrossAmountMinor">The period before any reduction.</param>
/// <param name="BuiltInDiscountMinor">What the price's automatic discount and the volume band took off.</param>
/// <param name="PromotionalDiscountMinor">What a promotional code took off after that.</param>
/// <param name="TaxAmountMinor">Tax on what was left, at this side's own rate and mode.</param>
/// <param name="PeriodTotalMinor">The whole period, tax included — gross less discounts, plus tax.</param>
/// <param name="ProratedValueMinor">
/// The part of that this settlement counts: unused time on the outgoing side, remaining time on the
/// target side.
/// </param>
public readonly record struct ProrationSide(
    long GrossAmountMinor,
    long BuiltInDiscountMinor,
    long PromotionalDiscountMinor,
    long TaxAmountMinor,
    long PeriodTotalMinor,
    long ProratedValueMinor);

/// <summary>
/// Both sides of a settlement and what closed the gap between them.
/// </summary>
/// <remarks>
/// A settlement is a subtraction, so a single gross-and-discount pair cannot describe it: the
/// subscriber is leaving one priced period part-way through and joining another, and both have their
/// own discounts and their own tax. <see cref="NetSettlementMinor"/> is target prorated value less
/// outgoing unused value less credit — negative when a downgrade banks credit rather than charging.
/// </remarks>
public readonly record struct ProrationBreakdown(
    ProrationSide Outgoing,
    ProrationSide Target,
    long CreditConsumedMinor,
    long NetSettlementMinor);
