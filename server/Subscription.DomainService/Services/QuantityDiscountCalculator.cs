using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Which volume band a quantity selects, and what that takes off the charge.
/// </summary>
/// <remarks>
/// Pure and static, like the amount and proration calculators. Every path that prices a quantity
/// — signup, renewal, proration preview, and a quantity change — resolves through here, so a
/// preview cannot quote one figure and the charge take another.
/// </remarks>
public static class QuantityDiscountCalculator
{
    /// <summary>
    /// The band <paramref name="quantity"/> falls in, and the arithmetic that follows from it.
    /// </summary>
    /// <remarks>
    /// A quantity matching no band is not an error: a plan authored without bands, which is every
    /// plan that existed before they did, resolves to no tier and no reduction, and prices exactly
    /// as it always has.
    /// </remarks>
    public static QuantityDiscountOutcome Resolve(
        IReadOnlyList<QuantityDiscountTier>? tiers,
        long unitAmountMinor,
        long quantity)
    {
        var gross = unitAmountMinor * quantity;

        if (tiers is null || tiers.Count == 0 || gross <= 0)
        {
            return new QuantityDiscountOutcome(null, 0, gross, 0, gross);
        }

        var tier = tiers.FirstOrDefault(candidate =>
            quantity >= candidate.MinimumQuantity &&
            (candidate.MaximumQuantity is not { } maximum || quantity <= maximum));

        if (tier is null || tier.DiscountBasisPoints <= 0)
        {
            return new QuantityDiscountOutcome(tier, tier?.DiscountBasisPoints ?? 0, gross, 0, gross);
        }

        var discount = gross * tier.DiscountBasisPoints / 10_000;

        return new QuantityDiscountOutcome(
            tier,
            tier.DiscountBasisPoints,
            gross,
            discount,
            gross - discount);
    }

    /// <summary>
    /// The band a plan snapshot selects for the quantities being priced.
    /// </summary>
    /// <remarks>
    /// Takes the plan, price and quantities explicitly rather than reading them off a
    /// subscription, so proration can price a hypothetical target — a different plan, or the same
    /// plan at a different quantity — through exactly this arithmetic. Read from a snapshot, never
    /// the catalogue: editing a plan's bands must not reprice anyone already holding them.
    /// <para>
    /// A price with no quantity item is a flat fee, and a flat fee has no quantity to band.
    /// </para>
    /// </remarks>
    public static QuantityDiscountOutcome ResolveFrom(
        PlanSnapshot plan,
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantityItems);

        if (string.IsNullOrWhiteSpace(price.QuantityItemKey))
        {
            return new QuantityDiscountOutcome(
                null, 0, price.UnitAmountMinor, 0, price.UnitAmountMinor);
        }

        var held = quantityItems
            .Where(item => string.Equals(item.ItemKey, price.QuantityItemKey, StringComparison.Ordinal))
            .ToList();

        // The amount snapshotted on the item, not the plan's current price: adding units later
        // charges what the subscriber agreed to.
        var unitAmount = held
            .Select(item => item.UnitAmountMinor)
            .DefaultIfEmpty(price.UnitAmountMinor)
            .First();

        var tiers = plan.QuantityItems
            .Find(item => string.Equals(item.ItemKey, price.QuantityItemKey, StringComparison.Ordinal))
            ?.QuantityDiscountTiers;

        return Resolve(tiers, unitAmount, held.Sum(item => item.Quantity));
    }
}

/// <summary>
/// What a quantity costs before and after its volume band, and which band that was.
/// </summary>
/// <remarks>
/// Carries the matched tier itself, not only the arithmetic, because an invoice has to be able to
/// explain which band produced a figure months later — and a catalogue edit will have moved the
/// bands by then.
/// </remarks>
public readonly record struct QuantityDiscountOutcome(
    QuantityDiscountTier? Tier,
    int DiscountBasisPoints,
    long GrossAmountMinor,
    long DiscountAmountMinor,
    long SubtotalMinor);
