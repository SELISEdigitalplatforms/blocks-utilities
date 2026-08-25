using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// The calendar-month rules: which prices may use them, where the boundaries fall, and what
/// fraction of a month a first period covers.
/// </summary>
/// <remarks>
/// Pure and static, like <see cref="BillingPeriodCalculator"/> — every instant is a parameter, so
/// a February boundary is as testable as an August one.
/// <para>
/// The recurring cadence itself needs nothing new. A calendar-aligned schedule is an ordinary
/// monthly <see cref="BillingSchedule"/> whose anchor happens to be the first of a month at local
/// midnight, so <see cref="BillingPeriodCalculator"/> derives every later boundary exactly as it
/// always has. Only the <em>first</em> period is special, and only because it starts mid-month.
/// </para>
/// </remarks>
public static class CalendarBillingAlignment
{
    /// <summary>
    /// Whether a cadence can be aligned to the calendar at all.
    /// </summary>
    /// <remarks>
    /// Monthly, once a month, and nothing else. A fortnight and a quarter both have boundaries
    /// that only sometimes land on a first, so aligning them would mean silently changing the
    /// cadence the author chose — the request is refused instead.
    /// </remarks>
    public static bool Supports(BillingInterval interval, int intervalCount) =>
        interval == BillingInterval.Month && intervalCount == 1;

    /// <summary>Whether this alignment is valid for this cadence.</summary>
    public static bool IsValid(
        BillingAlignment alignment,
        BillingInterval interval,
        int intervalCount) =>
        alignment != BillingAlignment.CalendarMonth || Supports(interval, intervalCount);

    /// <summary>Whether this alignment and cadence together mean calendar boundaries.</summary>
    public static bool IsCalendarAligned(
        BillingAlignment alignment,
        BillingInterval interval,
        int intervalCount) =>
        alignment == BillingAlignment.CalendarMonth && Supports(interval, intervalCount);

    /// <summary>Whether a snapshotted price actually bills on calendar boundaries.</summary>
    /// <remarks>
    /// Re-checks the cadence rather than trusting the stored alignment alone. A snapshot is only
    /// as good as what was validated when it was taken, and a subscription whose alignment and
    /// cadence disagree must bill the way its cadence says — never half-way between the two.
    /// </remarks>
    public static bool IsCalendarAligned(PriceSnapshot? price) =>
        price is not null &&
        IsCalendarAligned(price.BillingAlignment, price.Interval, price.IntervalCount);

    /// <summary>
    /// Builds the recurring schedule a calendar-aligned price renews on: local midnight, on the
    /// first, every month.
    /// </summary>
    /// <remarks>
    /// Anchored on the first of the month <paramref name="anchorInstantUtc"/> falls in — not the
    /// next one. The anchor only has to be *a* boundary for the derivation to be right, and using
    /// the current month keeps the first period's index at zero, where a reader expects it.
    /// </remarks>
    public static bool TryCreateSchedule(
        DateTime anchorInstantUtc,
        string timeZoneId,
        out BillingSchedule schedule)
    {
        schedule = new BillingSchedule();

        if (!BillingLocalTime.TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return false;
        }

        var local = BillingLocalTime.ToLocal(anchorInstantUtc, timeZone);
        var firstOfMonthLocal = new DateTime(local.Year, local.Month, 1);

        return BillingPeriodCalculator.TryCreateSchedule(
            BillingInterval.Month,
            1,
            BillingLocalTime.ToUtc(firstOfMonthLocal, timeZone),
            timeZoneId,
            out schedule);
    }

    /// <summary>
    /// The first period a calendar-aligned subscription gets, and what fraction of a month it is.
    /// </summary>
    /// <remarks>
    /// A signup on the local first is not a special case that happens to come to a whole month —
    /// it *is* a whole month, and is reported as unprorated so nothing downstream describes it as
    /// a partial period. Every other signup gets a stub running from the signup instant to the
    /// next local first.
    /// </remarks>
    public static bool TryResolveFirstPeriod(
        DateTime nowUtc,
        string timeZoneId,
        out CalendarFirstPeriod period)
    {
        period = default;

        if (!BillingLocalTime.TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return false;
        }

        var local = BillingLocalTime.ToLocal(nowUtc, timeZone);
        var daysInMonth = DateTime.DaysInMonth(local.Year, local.Month);
        var firstOfThisMonthLocal = new DateTime(local.Year, local.Month, 1);
        var nextFirstLocal = firstOfThisMonthLocal.AddMonths(1);
        var nextFirstUtc = BillingLocalTime.ToUtc(nextFirstLocal, timeZone);

        if (local.Day == 1)
        {
            period = new CalendarFirstPeriod(
                BillingLocalTime.ToUtc(firstOfThisMonthLocal, timeZone),
                nextFirstUtc,
                daysInMonth,
                daysInMonth,
                IsProrated: false);

            return true;
        }

        // Calendar dates, inclusive of the signup date itself: a subscriber signing up on the 25th
        // of a 31-day month has the 25th through the 31st, which is seven dates and not six. The
        // time of day never enters into it — everyone who signs up on the 25th buys the same seven
        // dates and pays the same fraction.
        var coveredDays = daysInMonth - local.Day + 1;

        period = new CalendarFirstPeriod(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            nextFirstUtc,
            coveredDays,
            daysInMonth,
            IsProrated: true);

        return true;
    }

    /// <summary>
    /// The fraction frozen onto a subscription when its first period was priced.
    /// </summary>
    /// <remarks>
    /// Read back rather than recalculated, so anything settling that first charge later — an
    /// activation, a recovery sweep — describes the period the customer actually bought and not
    /// the one today's date would produce.
    /// </remarks>
    public static BillingDayFraction FrozenFraction(SubscriptionDetail subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return subscription is
            { InitialChargeProrated: true, ProrationDays: { } covered, ProrationTotalDays: { } total }
            ? new BillingDayFraction(covered, total)
            : default;
    }

    /// <summary>
    /// Scales an amount by a period's day fraction, rounded to the nearest minor unit with halves
    /// away from zero.
    /// </summary>
    /// <remarks>
    /// Exact integer arithmetic widened to <see cref="Int128"/> for the multiplication, matching
    /// <see cref="SubscriptionAmountCalculator"/>'s tax split — this module's money never touches a
    /// floating-point ratio, and a day fraction is no reason to start.
    /// </remarks>
    public static long Prorate(long amountMinor, int coveredDays, int totalDays)
    {
        if (totalDays <= 0 || coveredDays >= totalDays)
        {
            return amountMinor;
        }

        if (coveredDays <= 0 || amountMinor == 0)
        {
            return 0;
        }

        var scaled = ((Int128)Math.Abs(amountMinor) * coveredDays + (totalDays / 2)) / totalDays;

        return amountMinor < 0 ? -(long)scaled : (long)scaled;
    }
}

/// <summary>
/// How much of a whole period a charge covers, as calendar dates.
/// </summary>
/// <remarks>
/// <c>default</c> is a whole period — <see cref="TotalDays"/> of zero means "nothing to scale by"
/// — so every existing call site that does not know about proration keeps charging full periods
/// without naming a fraction it does not have.
/// </remarks>
/// <param name="CoveredDays">The 7 of "7/31".</param>
/// <param name="TotalDays">The 31 of "7/31". Zero or less means the period is whole.</param>
public readonly record struct BillingDayFraction(int CoveredDays, int TotalDays)
{
    /// <summary>Whether this actually scales anything down.</summary>
    public bool IsPartial => TotalDays > 0 && CoveredDays < TotalDays && CoveredDays >= 0;

    /// <summary>Scales an amount by this fraction, or returns it untouched when whole.</summary>
    public long Apply(long amountMinor) =>
        CalendarBillingAlignment.Prorate(amountMinor, CoveredDays, TotalDays);

    /// <summary>The fraction a resolved first period represents.</summary>
    public static BillingDayFraction Of(CalendarFirstPeriod period) =>
        period.IsProrated
            ? new BillingDayFraction(period.CoveredDays, period.TotalDays)
            : default;
}

/// <summary>
/// A calendar-aligned subscription's opening period.
/// </summary>
/// <param name="CoveredDays">Calendar dates this period actually covers — the 7 of "7/31".</param>
/// <param name="TotalDays">Dates in the month it is a fraction of — the 31 of "7/31".</param>
/// <param name="IsProrated">
/// False when the period is a whole month, so a full first period is never reported as a stub.
/// </param>
public readonly record struct CalendarFirstPeriod(
    DateTime StartUtc,
    DateTime EndUtc,
    int CoveredDays,
    int TotalDays,
    bool IsProrated);
