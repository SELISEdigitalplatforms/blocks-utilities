using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// What a meter's usage costs beyond its included quantity, in minor units.
/// </summary>
/// <remarks>
/// Pure and static, like the other calculators in this module. Only the *overage* is priced —
/// <c>usage − IncludedQuantity</c> — so <see cref="PlanMeter.IncludedQuantity"/> stays the one
/// place a plan's free allowance lives; a rate table never needs a zero-cost first tier to
/// represent it.
/// <para>
/// Tier bounds are counted from the first overage unit, inclusive: a tier with
/// <c>UpToQuantity = 400</c> covers overage units 1 through 400, and the next tier starts at
/// overage unit 401. The final tier (<c>UpToQuantity = null</c>) absorbs whatever remains.
/// </para>
/// </remarks>
public static class SubscriptionUsageRater
{
    public static long OverageAmountMinor(PlanMeter meter, long balance, string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(meter);

        if (!meter.OverageAllowed)
        {
            return 0;
        }

        var overageUnits = Math.Max(0, balance - meter.IncludedQuantity);

        if (overageUnits == 0)
        {
            return 0;
        }

        var table = meter.RateTables.Find(table =>
            string.Equals(table.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));

        // A meter with no rate table for this subscription's currency cannot be priced. Skipping
        // it is a plan-authoring gap to fix, not a reason to fail every other meter's charge.
        if (table is null)
        {
            return 0;
        }

        return WalkTiers(table.Tiers, overageUnits);
    }

    private static long WalkTiers(List<MeterTier> tiers, long overageUnits)
    {
        var previousBound = 0L;
        var remaining = overageUnits;
        Int128 total = 0;

        foreach (var tier in tiers)
        {
            if (remaining <= 0)
            {
                break;
            }

            var bandWidth = tier.UpToQuantity is { } upTo
                ? Math.Max(0, upTo - previousBound)
                : remaining;
            var bandUnits = Math.Min(remaining, bandWidth);

            total += (Int128)bandUnits * tier.UnitAmountMinor;
            remaining -= bandUnits;
            previousBound = tier.UpToQuantity ?? previousBound;
        }

        return (long)total;
    }
}
