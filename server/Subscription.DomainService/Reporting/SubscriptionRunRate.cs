using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// One seat-count band, as the reports group by.
/// </summary>
public sealed record SeatTierBand(string Name, int MinimumSeats, int? MaximumSeats);

/// <summary>
/// Turns a subscription's price and quantity into a monthly run-rate and a seat band.
/// </summary>
/// <remarks>
/// Public and pure so the bands and the interval arithmetic can be exercised directly. Neither is
/// obvious enough to leave untested: a band boundary is off by one in whichever direction nobody
/// checked, and a yearly price divided by the wrong number is a run-rate that is wrong by an order
/// of magnitude while still looking like money.
/// </remarks>
public static class SubscriptionRunRate
{
    /// <summary>
    /// The bands, lowest first. Fixed rather than configurable because they are a reporting
    /// convention here, not a billing rule — nothing is priced off them.
    /// </summary>
    public static IReadOnlyList<SeatTierBand> Tiers { get; } =
    [
        new("1", 1, 1),
        new("2-3", 2, 3),
        new("4-9", 4, 9),
        new("10-24", 10, 24),
        new("25-40", 25, 40),
        new("41+", 41, null)
    ];

    /// <summary>
    /// The band a seat count falls in.
    /// </summary>
    /// <remarks>
    /// A count below one lands in the first band rather than in none. A subscription always
    /// occupies at least one seat conceptually — a flat-priced plan carries no quantity items at
    /// all — and dropping it from the report entirely would make the tier counts fail to sum to
    /// the subscription count, which is the one arithmetic check a reader can perform.
    /// </remarks>
    public static SeatTierBand TierFor(long seats) =>
        Tiers.FirstOrDefault(
            tier => seats >= tier.MinimumSeats &&
                    (tier.MaximumSeats is not { } maximum || seats <= maximum))
        ?? Tiers[0];

    /// <summary>
    /// How many units the subscription is billed for.
    /// </summary>
    /// <remarks>
    /// Counts only the quantity items the price actually charges on, matching how
    /// <c>SubscriptionAmountCalculator.GrossAmountMinor</c> decides what to multiply. A price with
    /// no quantity item key is flat, and counts as one seat — it bills a single subscription, not
    /// zero of something.
    /// </remarks>
    public static long BillableSeats(
        PriceSnapshot price,
        IReadOnlyList<SubscriptionQuantityItem> quantityItems)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantityItems);

        if (string.IsNullOrWhiteSpace(price.QuantityItemKey))
        {
            return 1;
        }

        var quantity = quantityItems
            .Where(item => string.Equals(
                item.ItemKey,
                price.QuantityItemKey,
                StringComparison.Ordinal))
            .Sum(item => item.Quantity);

        return Math.Max(1, quantity);
    }

    /// <summary>
    /// What one period's charge is worth per month.
    /// </summary>
    /// <remarks>
    /// A year is twelve months and a month is one; a week and a day are converted at 52 and 365 to
    /// the year, which is the ordinary run-rate convention and the only one that keeps a weekly and
    /// a daily plan comparable with each other.
    /// <para>
    /// Integer arithmetic throughout, widened to <see cref="Int128"/> for the multiplication for
    /// the same reason the tax and proration maths are — an amount times 365 overflows a
    /// <see cref="long"/> long before the amounts involved look unreasonable — and rounded to the
    /// nearest minor unit rather than truncated, so a run-rate summed over many subscriptions does
    /// not drift low by a fraction of a unit each time.
    /// </para>
    /// </remarks>
    public static long MonthlyAmountMinor(
        long periodAmountMinor,
        BillingInterval interval,
        int intervalCount)
    {
        if (periodAmountMinor == 0)
        {
            return 0;
        }

        // A zero or negative count is not a period anyone can be billed on. Treating it as one
        // keeps a malformed snapshot out of the totals as itself rather than as a divide by zero.
        var count = Math.Max(1, intervalCount);

        var (numerator, denominator) = interval switch
        {
            BillingInterval.Day => (365L, 12L * count),
            BillingInterval.Week => (52L, 12L * count),
            BillingInterval.Month => (1L, (long)count),
            BillingInterval.Year => (1L, 12L * count),
            _ => (1L, (long)count)
        };

        var scaled = ((Int128)periodAmountMinor * numerator) + (denominator / 2);

        return (long)(scaled / denominator);
    }

    /// <summary>Twelve months of a monthly run-rate, so annual and monthly can never disagree.</summary>
    public static long AnnualFromMonthly(long monthlyMinor) => monthlyMinor * 12;
}
