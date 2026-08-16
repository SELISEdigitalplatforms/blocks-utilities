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
/// <see cref="SubscriptionAmountCalculator.TaxAmountMinor"/>), just against two different
/// price/quantity pairs. The discount is the subscriber's, not the plan's, so it applies to both
/// sides identically — but tax is each side's own price's rate, since a plan change can move the
/// subscriber to a differently-taxed price.
/// </remarks>
public static class SubscriptionProrationCalculator
{
    public static ProrationOutcome Calculate(
        SubscriptionDetail subscription,
        PriceSnapshot targetPrice,
        IReadOnlyList<SubscriptionQuantityItem> targetQuantityItems,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);
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

        var oldGross = SubscriptionAmountCalculator.GrossAmountMinor(
            subscription.Price,
            subscription.QuantityItems);
        var oldDiscounted = SubscriptionAmountCalculator.ApplyDiscount(
            oldGross,
            subscription.Discount,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        var newGross = SubscriptionAmountCalculator.GrossAmountMinor(targetPrice, targetQuantityItems);
        var newDiscounted = SubscriptionAmountCalculator.ApplyDiscount(
            newGross,
            subscription.Discount,
            subscription.DiscountPeriodsApplied,
            nowUtc);

        // Each side taxed at its own price's rate — not necessarily the same rate, if the plan
        // change also moves the subscriber to a differently-taxed price.
        var oldTaxInclusive = oldDiscounted.AmountMinor + SubscriptionAmountCalculator.TaxAmountMinor(
            oldDiscounted.AmountMinor,
            subscription.Price.TaxRateBasisPoints);
        var newTaxInclusive = newDiscounted.AmountMinor + SubscriptionAmountCalculator.TaxAmountMinor(
            newDiscounted.AmountMinor,
            targetPrice.TaxRateBasisPoints);

        var oldRemainingValue = Prorate(oldTaxInclusive, remainingTicks, totalTicks);
        var newRemainingCost = Prorate(newTaxInclusive, remainingTicks, totalTicks);

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
