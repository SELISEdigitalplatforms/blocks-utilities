using Subscription.DomainService.Entities;

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
        DateTime targetPeriodEndUtc)
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

        var newDiscounted = SubscriptionAmountCalculator.DiscountedAmountMinor(
            targetPlan,
            subscription.Discount,
            targetPrice,
            targetQuantityItems,
            subscription.DiscountPeriodsApplied,
            nowUtc);

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
        var newRemainingCost = targetTotalTicks <= 0
            ? 0
            : Prorate(newTaxInclusive, targetRemainingTicks, targetTotalTicks);

        var rawDelta = newRemainingCost - oldRemainingValue;
        var netAfterCredit = rawDelta - subscription.CreditBalanceMinor;

        return netAfterCredit > 0
            ? new ProrationOutcome(netAfterCredit, 0)
            : new ProrationOutcome(0, -netAfterCredit);
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
public readonly record struct ProrationOutcome(long ChargeMinor, long NewCreditBalanceMinor);
