using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Discounts and taxes a metered overage amount, exactly the way period-end usage rating charges
/// it (<see cref="Outbox.SubscriptionUsageRatingProcessor"/>).
/// </summary>
/// <remarks>
/// Extracted so the overage preview and the final invoice cannot drift apart: both call this one
/// method with the same price snapshot, so a subscriber previewing an upgrade sees the figure the
/// period-end invoice would actually charge, not a second calculation that happens to usually
/// agree with it.
/// </remarks>
public static class UsageChargeCalculator
{
    public static UsageCharge Charge(long grossAmountMinor, PriceSnapshot price)
    {
        ArgumentNullException.ThrowIfNull(price);

        // The price's automatic discount applies to what the price charges, and overage is one of
        // the things it charges. Through the shared calculator with no volume band — a band prices
        // seats and has no meaning for metered units, so the synthetic pass-through outcome below
        // is empty and both combination policies agree.
        var builtIn = BuiltInDiscountCalculator.Resolve(
            grossAmountMinor,
            new QuantityDiscountOutcome(null, 0, grossAmountMinor, 0, grossAmountMinor),
            price.AutomaticDiscountBasisPoints,
            price.QuantityDiscountCombination);

        // Tax is on the aggregate, after the discount, at the subscription's own snapshotted rate
        // and mode — the same "one charge, taxed the way it was sold" scope period-end rating uses.
        var breakdown = SubscriptionAmountCalculator.TaxBreakdownFor(
            builtIn.SubtotalMinor,
            price.TaxRateBasisPoints,
            price.TaxMode);

        return new UsageCharge(
            grossAmountMinor,
            builtIn.DiscountAmountMinor,
            breakdown.NetAmountMinor,
            breakdown.TaxAmountMinor,
            breakdown.TotalAmountMinor);
    }

    /// <summary>
    /// What an additional slice of usage would add, found as the difference between two fully
    /// rated totals rather than rated on its own.
    /// </summary>
    /// <remarks>
    /// A tier boundary crossed by the additional units, or a rounding step at the discount or tax
    /// boundary, can price the same units differently depending on what came before them in the
    /// period. Rating the difference of two whole-period charges is the only way to guarantee the
    /// additional figure matches what the period-end invoice would actually add.
    /// </remarks>
    public static UsageCharge Difference(UsageCharge projected, UsageCharge baseline) => new(
        projected.GrossMinor - baseline.GrossMinor,
        projected.AutomaticDiscountMinor - baseline.AutomaticDiscountMinor,
        projected.NetMinor - baseline.NetMinor,
        projected.TaxMinor - baseline.TaxMinor,
        projected.TotalMinor - baseline.TotalMinor);
}

/// <summary>
/// One charge, broken into what it grossed, what was discounted off it, and how the remainder
/// split into net and tax. <see cref="TotalMinor"/> is always <see cref="NetMinor"/> plus
/// <see cref="TaxMinor"/>, by construction.
/// </summary>
public readonly record struct UsageCharge(
    long GrossMinor,
    long AutomaticDiscountMinor,
    long NetMinor,
    long TaxMinor,
    long TotalMinor);
