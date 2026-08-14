using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Works out which period an instant falls in, and when that period ends.
/// </summary>
/// <remarks>
/// Pure and static: the instant is always a parameter, never read from the clock, so every
/// boundary is reproducible and testable years either side of today.
/// <para>
/// Periods are <em>derived</em>, never advanced. Nothing has to notice a boundary passing —
/// the moment it does, the same call simply returns the next period. That is what lets metered
/// usage roll over without a scheduled job, and removes the class of bug where a rollover runs
/// twice or not at all.
/// </para>
/// </remarks>
public static class BillingPeriodCalculator
{
    /// <summary>
    /// Guards the walk that corrects an estimated period index. The estimate is out by at most
    /// one in normal use; anything further means the inputs are not what this assumes.
    /// </summary>
    private const int MaximumIndexCorrections = 8;

    /// <summary>
    /// Builds a schedule from a starting instant, preserving the day the subscriber chose.
    /// </summary>
    /// <remarks>
    /// The anchor day is taken from the local calendar, not from UTC: a subscription created at
    /// 00:30 in Zurich starts on the 1st there and on the 31st in UTC, and the customer's
    /// calendar is the one their invoice follows.
    /// </remarks>
    public static bool TryCreateSchedule(
        BillingInterval interval,
        int intervalCount,
        DateTime anchorInstantUtc,
        string timeZoneId,
        out BillingSchedule schedule)
    {
        schedule = new BillingSchedule();

        if (intervalCount < 1 ||
            !BillingLocalTime.TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return false;
        }

        var anchorLocal = BillingLocalTime.ToLocal(anchorInstantUtc, timeZone);

        schedule = new BillingSchedule
        {
            Interval = interval,
            IntervalCount = intervalCount,
            AnchorInstantUtc = DateTime.SpecifyKind(anchorInstantUtc, DateTimeKind.Utc),
            TimeZoneId = timeZoneId,
            AnchorDayOfMonth = anchorLocal.Day,
            AnchorMinutesFromMidnight =
                (anchorLocal.Hour * 60) + anchorLocal.Minute
        };

        return true;
    }

    /// <summary>
    /// Finds the period containing <paramref name="instantUtc"/>.
    /// </summary>
    /// <returns>
    /// False when the schedule cannot be resolved — an unknown time zone, or a cadence of less
    /// than one. Failing closed here keeps a misconfigured subscription from throwing on a path
    /// that is called on every gated action.
    /// </returns>
    public static bool TryGetPeriod(
        BillingSchedule schedule,
        DateTime instantUtc,
        out BillingPeriod period)
    {
        period = default;

        if (schedule is null ||
            schedule.IntervalCount < 1 ||
            !BillingLocalTime.TryFindTimeZone(schedule.TimeZoneId, out var timeZone))
        {
            return false;
        }

        var instant = DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc);
        var anchorLocal = BillingLocalTime.ToLocal(schedule.AnchorInstantUtc, timeZone);
        var index = CorrectIndex(
            schedule,
            timeZone,
            anchorLocal,
            instant,
            EstimateIndex(schedule, timeZone, anchorLocal, instant));

        var startUtc = BoundaryOf(schedule, timeZone, anchorLocal, index);
        var endUtc = BoundaryOf(schedule, timeZone, anchorLocal, index + 1);

        period = new BillingPeriod(
            index,
            startUtc,
            endUtc,
            PeriodKey.Create(schedule.Interval, startUtc));

        return true;
    }

    /// <summary>
    /// The instant the given period boundary falls on.
    /// </summary>
    /// <remarks>
    /// The month-end rule lives here, and it is the one worth stating: the chosen day is
    /// clamped to the length of the target month on every read and never written back. A
    /// subscription anchored on the 31st bills on the 28th in February and returns to the 31st
    /// in March. Storing February's clamp instead would drag every later period earlier for the
    /// life of the subscription — a drift nobody notices until a customer counts their invoices.
    /// </remarks>
    private static DateTime BoundaryOf(
        BillingSchedule schedule,
        TimeZoneInfo timeZone,
        DateTime anchorLocal,
        int index)
    {
        var offset = index * schedule.IntervalCount;

        var local = schedule.Interval switch
        {
            BillingInterval.Day =>
                anchorLocal.Date.AddDays(offset),
            BillingInterval.Week =>
                anchorLocal.Date.AddDays(offset * 7L),
            BillingInterval.Month =>
                MonthBoundary(schedule, anchorLocal, offset),
            BillingInterval.Year =>
                MonthBoundary(schedule, anchorLocal, offset * 12L),
            _ => anchorLocal.Date
        };

        return BillingLocalTime.ToUtc(
            local.AddMinutes(schedule.AnchorMinutesFromMidnight),
            timeZone);
    }

    private static DateTime MonthBoundary(
        BillingSchedule schedule,
        DateTime anchorLocal,
        long monthOffset)
    {
        var target = new DateTime(anchorLocal.Year, anchorLocal.Month, 1)
            .AddMonths((int)monthOffset);

        var day = Math.Min(
            schedule.AnchorDayOfMonth,
            DateTime.DaysInMonth(target.Year, target.Month));

        return new DateTime(target.Year, target.Month, day);
    }

    /// <summary>
    /// A cheap first guess at the period index, refined by <see cref="CorrectIndex"/>.
    /// </summary>
    /// <remarks>
    /// Estimating in the local calendar and then correcting is deliberate: a clamped month end
    /// and a daylight-saving shift both move a boundary by less than a whole period, so any
    /// closed-form answer is occasionally out by one and always in a way that is hard to see.
    /// </remarks>
    private static int EstimateIndex(
        BillingSchedule schedule,
        TimeZoneInfo timeZone,
        DateTime anchorLocal,
        DateTime instantUtc)
    {
        var local = BillingLocalTime.ToLocal(instantUtc, timeZone);

        var elapsed = schedule.Interval switch
        {
            BillingInterval.Day =>
                (long)(local.Date - anchorLocal.Date).TotalDays,
            BillingInterval.Week =>
                (long)(local.Date - anchorLocal.Date).TotalDays / 7,
            BillingInterval.Month =>
                MonthsBetween(anchorLocal, local),
            BillingInterval.Year =>
                MonthsBetween(anchorLocal, local) / 12,
            _ => 0
        };

        return (int)FloorDivide(elapsed, schedule.IntervalCount);
    }

    private static int CorrectIndex(
        BillingSchedule schedule,
        TimeZoneInfo timeZone,
        DateTime anchorLocal,
        DateTime instantUtc,
        int estimate)
    {
        var index = estimate;

        for (var correction = 0; correction < MaximumIndexCorrections; correction++)
        {
            if (BoundaryOf(schedule, timeZone, anchorLocal, index) > instantUtc)
            {
                index--;

                continue;
            }

            if (BoundaryOf(schedule, timeZone, anchorLocal, index + 1) <= instantUtc)
            {
                index++;

                continue;
            }

            break;
        }

        return index;
    }

    private static long MonthsBetween(DateTime from, DateTime to) =>
        (((long)to.Year - from.Year) * 12) + (to.Month - from.Month);

    /// <summary>
    /// Division that rounds towards negative infinity, so an instant before the anchor lands in
    /// a period rather than being rounded back into the first one.
    /// </summary>
    private static long FloorDivide(long dividend, long divisor)
    {
        var quotient = Math.DivRem(dividend, divisor, out var remainder);

        return remainder != 0 && ((remainder < 0) != (divisor < 0))
            ? quotient - 1
            : quotient;
    }
}
