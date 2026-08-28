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

        return OverageAllocations(
            meter,
            Math.Max(0, balance - meter.IncludedQuantity),
            currencyCode).TotalAmountMinor;
    }

    /// <summary>
    /// Prices a range of overage units and reports which tier band each unit fell into.
    /// </summary>
    /// <param name="overageUnits">
    /// The overage, already computed by the caller — from the plan's own included quantity for
    /// period-end rating, or from an effective allowance (trial grant, carry-forward) for a
    /// preview. Kept separate from a raw balance so both callers can agree on what "overage" means
    /// without this method ever reading <see cref="PlanMeter.IncludedQuantity"/> itself.
    /// </param>
    /// <param name="fromOverageUnitsExclusive">
    /// Only the tier bands covering units after this point are priced and reported — the "already
    /// billed" prefix of a preview's projected range, so its allocations describe only the
    /// hypothetical addition rather than the whole period.
    /// </param>
    public static UsageTierAllocationResult OverageAllocations(
        PlanMeter meter,
        long overageUnits,
        string currencyCode,
        long fromOverageUnitsExclusive = 0)
    {
        ArgumentNullException.ThrowIfNull(meter);

        if (!meter.OverageAllowed || overageUnits <= fromOverageUnitsExclusive)
        {
            return new UsageTierAllocationResult(0, []);
        }

        var table = meter.RateTables.Find(table =>
            string.Equals(table.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));

        // A meter with no rate table for this subscription's currency cannot be priced. Skipping
        // it is a plan-authoring gap to fix, not a reason to fail every other meter's charge. A
        // preview refuses outright instead — see subscription_meter_rate_unavailable — since a
        // hypothetical quote of zero would be misleading rather than merely incomplete.
        if (table is null)
        {
            return new UsageTierAllocationResult(0, []);
        }

        return WalkTierRange(table.Tiers, fromOverageUnitsExclusive, overageUnits);
    }

    private static UsageTierAllocationResult WalkTierRange(
        List<MeterTier> tiers,
        long fromOverageUnitsExclusive,
        long toOverageUnitsInclusive)
    {
        var previousBound = 0L;
        Int128 total = 0;
        List<TierAllocation>? allocations = null;

        foreach (var tier in tiers)
        {
            if (previousBound >= toOverageUnitsInclusive)
            {
                break;
            }

            var tierEnd = tier.UpToQuantity is { } upTo
                ? Math.Min(upTo, toOverageUnitsInclusive)
                : toOverageUnitsInclusive;

            if (tierEnd > previousBound)
            {
                var rangeStart = Math.Max(previousBound, fromOverageUnitsExclusive);

                if (tierEnd > rangeStart)
                {
                    var units = tierEnd - rangeStart;
                    var amount = (Int128)units * tier.UnitAmountMinor;
                    total += amount;
                    (allocations ??= []).Add(new TierAllocation(
                        rangeStart + 1,
                        tierEnd,
                        units,
                        tier.UnitAmountMinor,
                        (long)amount));
                }
            }

            previousBound = tier.UpToQuantity ?? tierEnd;
        }

        return new UsageTierAllocationResult((long)total, allocations ?? []);
    }
}

/// <summary>
/// One tier band's slice of a priced overage range: which overage units it covered, at what rate.
/// </summary>
/// <param name="FromOverageQuantity">
/// The first overage unit this band covers, counted from the first overage unit overall (1),
/// not from wherever the priced range started.
/// </param>
/// <param name="ToOverageQuantity">The last overage unit this band covers, inclusive.</param>
public readonly record struct TierAllocation(
    long FromOverageQuantity,
    long ToOverageQuantity,
    long Units,
    long UnitAmountMinor,
    long AmountMinor);

/// <summary>A priced range's total, and the bands that made it up.</summary>
public readonly record struct UsageTierAllocationResult(
    long TotalAmountMinor,
    IReadOnlyList<TierAllocation> Allocations);
