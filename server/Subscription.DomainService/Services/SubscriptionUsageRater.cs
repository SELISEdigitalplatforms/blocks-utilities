using Subscription.DomainService.Entities;
using Subscription.DomainService.Utilities;

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
/// Tier bands are closed above and open below — <c>(previousBound, UpToQuantity]</c> — so a
/// fractional overage falls into exactly one of them. A band's reported first quantity is the
/// smallest one its meter can distinguish above that open bound, which at scale zero is the whole
/// unit the band has always started at: a tier with <c>UpToQuantity = 400</c> still reports units 1
/// through 400 on a whole-unit meter, and the next band still starts at 401.
/// </para>
/// <para>
/// Quantities are exact decimals and so are the per-band amounts. The single rounding event is the
/// meter's own total, in <see cref="MeterQuantity.ToMinorUnits"/> — so re-banding a rate table
/// without changing any of its prices cannot change the bill, and a band breakdown always sums to
/// the figure that was rounded.
/// </para>
/// </remarks>
public static class SubscriptionUsageRater
{
    public static long OverageAmountMinor(PlanMeter meter, decimal balance, string currencyCode)
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
        decimal overageUnits,
        string currencyCode,
        decimal fromOverageUnitsExclusive = 0)
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

        return WalkTierRange(
            table.Tiers,
            meter.QuantityScale,
            fromOverageUnitsExclusive,
            overageUnits);
    }

    /// <summary>
    /// Walks the tier table in exact decimal arithmetic throughout, rounding once at the end.
    /// </summary>
    /// <remarks>
    /// A rate table's unit amount and a technically valid (merely very large) quantity can each
    /// pass validation on their own and still multiply or sum into something no <c>long</c>
    /// minor-unit amount can hold. Decimal arithmetic raises <see cref="OverflowException"/> of its
    /// own accord rather than wrapping, and the final narrowing cast in
    /// <see cref="MeterQuantity.ToMinorUnits"/> is checked, so an amount that cannot be represented
    /// is refused rather than mispriced. The exception propagates to the caller, which is
    /// responsible for turning it into a refusal (the preview) or a deferral (period-end rating).
    /// </remarks>
    private static UsageTierAllocationResult WalkTierRange(
        List<MeterTier> tiers,
        int quantityScale,
        decimal fromOverageUnitsExclusive,
        decimal toOverageUnitsInclusive)
    {
        var previousBound = 0m;
        var total = 0m;
        List<TierAllocation>? allocations = null;
        var step = MeterQuantity.SmallestStep(quantityScale);

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
                    var amount = units * tier.UnitAmountMinor;
                    total += amount;
                    (allocations ??= []).Add(new TierAllocation(
                        rangeStart + step,
                        tierEnd,
                        units,
                        tier.UnitAmountMinor,
                        amount));
                }
            }

            previousBound = tier.UpToQuantity ?? tierEnd;
        }

        return new UsageTierAllocationResult(
            MeterQuantity.ToMinorUnits(total),
            allocations ?? []);
    }
}

/// <summary>
/// One tier band's slice of a priced overage range: which overage units it covered, at what rate.
/// </summary>
/// <param name="FromOverageQuantity">
/// The first quantity this band covers, counted from the first overage unit overall rather than
/// from wherever the priced range started. The band's lower bound is exclusive, so this is the
/// smallest quantity above it the meter can distinguish — the whole unit itself on a whole-unit
/// meter.
/// </param>
/// <param name="ToOverageQuantity">The last overage quantity this band covers, inclusive.</param>
/// <param name="AmountMinor">
/// Exact, and so possibly fractional: a third of a unit priced at one minor unit costs a third of
/// one. Only a meter's total is rounded to whole minor units, which is why these sum to the figure
/// that was rounded rather than to the rounded figure itself.
/// </param>
public readonly record struct TierAllocation(
    decimal FromOverageQuantity,
    decimal ToOverageQuantity,
    decimal Units,
    long UnitAmountMinor,
    decimal AmountMinor);

/// <summary>A priced range's total in whole minor units, and the bands that made it up.</summary>
public readonly record struct UsageTierAllocationResult(
    long TotalAmountMinor,
    IReadOnlyList<TierAllocation> Allocations);
