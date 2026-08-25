using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The calendar rules themselves: which cadences may use them, where the stub falls, and what
/// fraction of a month it is.
/// </summary>
/// <remarks>
/// Every number here is one a customer can check against their own calendar, which is the point
/// of counting dates rather than elapsed time. A month is not 30 days, and a subscriber who joins
/// on the 25th of August has bought seven dates whether they signed up at breakfast or at
/// midnight.
/// </remarks>
public sealed class CalendarBillingAlignmentTests
{
    private const string Zurich = "Europe/Zurich";

    [Theory]
    [InlineData(BillingInterval.Month, 1, true)]
    [InlineData(BillingInterval.Month, 3, false)]
    [InlineData(BillingInterval.Month, 12, false)]
    [InlineData(BillingInterval.Day, 1, false)]
    [InlineData(BillingInterval.Week, 1, false)]
    [InlineData(BillingInterval.Year, 1, false)]
    public void Only_a_single_month_cadence_can_be_aligned_to_the_calendar(
        BillingInterval interval,
        int intervalCount,
        bool supported)
    {
        CalendarBillingAlignment.Supports(interval, intervalCount).Should().Be(supported);

        CalendarBillingAlignment
            .IsValid(BillingAlignment.CalendarMonth, interval, intervalCount)
            .Should().Be(supported);

        CalendarBillingAlignment
            .IsValid(BillingAlignment.Anniversary, interval, intervalCount)
            .Should().BeTrue("an anniversary price has no cadence it cannot describe");
    }

    [Fact]
    public void A_signup_on_the_25th_of_a_31_day_month_buys_seven_dates()
    {
        Resolve(new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc), out var period);

        period.IsProrated.Should().BeTrue();
        period.CoveredDays.Should().Be(7, "the 25th through the 31st is seven dates, not six");
        period.TotalDays.Should().Be(31);
        period.EndUtc.Should().Be(LocalMidnight(2026, 9, 1));
    }

    /// <summary>
    /// The one that makes the fraction defensible: two customers who joined on the same date pay
    /// the same, whatever the clock said. Anything else would mean a 23:59 signup buying a day it
    /// had a minute of.
    /// </summary>
    [Fact]
    public void The_time_of_day_never_changes_the_fraction()
    {
        // 24 August 22:05 UTC is already the 25th in Zurich, and 25 August 21:59 UTC still is.
        Resolve(new DateTime(2026, 8, 24, 22, 5, 0, DateTimeKind.Utc), out var justAfterMidnight);
        Resolve(new DateTime(2026, 8, 25, 21, 59, 0, DateTimeKind.Utc), out var lateEvening);

        justAfterMidnight.CoveredDays.Should().Be(7);
        lateEvening.CoveredDays.Should().Be(7);
        justAfterMidnight.EndUtc.Should().Be(lateEvening.EndUtc);
    }

    [Theory]
    [InlineData(2026, 2, 10, 19, 28)]
    [InlineData(2028, 2, 10, 20, 29)]
    [InlineData(2026, 4, 15, 16, 30)]
    public void The_denominator_is_the_length_of_the_month_actually_being_split(
        int year,
        int month,
        int day,
        int expectedCoveredDays,
        int expectedTotalDays)
    {
        Resolve(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc), out var period);

        period.CoveredDays.Should().Be(expectedCoveredDays);
        period.TotalDays.Should().Be(expectedTotalDays);
    }

    [Fact]
    public void A_signup_on_the_local_first_is_a_whole_month_and_is_not_called_prorated()
    {
        Resolve(new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), out var period);

        period.IsProrated.Should().BeFalse(
            "a full first period described as a stub would have every client showing a " +
            "proration that did not happen");
        period.CoveredDays.Should().Be(30);
        period.TotalDays.Should().Be(30);
        period.StartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        period.EndUtc.Should().Be(LocalMidnight(2026, 10, 1));
        BillingDayFraction.Of(period).IsPartial.Should().BeFalse();
    }

    /// <summary>
    /// Zurich runs an hour or two ahead of UTC, so late-evening UTC instants are already tomorrow
    /// there. The subscriber's own calendar decides, not the server's.
    /// </summary>
    [Fact]
    public void The_local_calendar_decides_the_date_not_utc()
    {
        // 31 August 23:00 UTC is 1 September 01:00 in Zurich: a whole month, not a one-day stub.
        Resolve(new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc), out var period);

        period.IsProrated.Should().BeFalse();
        period.TotalDays.Should().Be(30, "it is already September there");
    }

    [Fact]
    public void A_boundary_across_a_daylight_saving_change_is_still_a_whole_calendar_month()
    {
        // Zurich springs forward on 29 March 2026 and back on 25 October 2026.
        Resolve(new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc), out var march);
        Resolve(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), out var october);

        march.CoveredDays.Should().Be(12);
        march.TotalDays.Should().Be(31, "March has 31 dates however many hours they add up to");
        march.EndUtc.Should().Be(LocalMidnight(2026, 4, 1));

        october.CoveredDays.Should().Be(12);
        october.TotalDays.Should().Be(31);
        october.EndUtc.Should().Be(LocalMidnight(2026, 11, 1));
    }

    [Fact]
    public void The_recurring_schedule_lands_on_the_first_at_local_midnight()
    {
        CalendarBillingAlignment
            .TryCreateSchedule(
                new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc), Zurich, out var schedule)
            .Should().BeTrue();

        schedule.AnchorDayOfMonth.Should().Be(1);
        schedule.AnchorMinutesFromMidnight.Should().Be(0);
        schedule.Interval.Should().Be(BillingInterval.Month);
        schedule.IntervalCount.Should().Be(1);

        // Derived, not remembered: every later boundary falls out of the same anchor.
        BillingPeriodCalculator
            .TryGetPeriod(
                schedule, new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), out var september)
            .Should().BeTrue();

        september.StartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        september.EndUtc.Should().Be(LocalMidnight(2026, 10, 1));

        BillingPeriodCalculator
            .TryGetPeriod(
                schedule, new DateTime(2027, 2, 15, 0, 0, 0, DateTimeKind.Utc), out var february)
            .Should().BeTrue();

        february.StartUtc.Should().Be(LocalMidnight(2027, 2, 1));
        february.EndUtc.Should().Be(LocalMidnight(2027, 3, 1),
            "a short month still runs first to first");
    }

    [Fact]
    public void An_unknown_time_zone_fails_closed_rather_than_throwing()
    {
        CalendarBillingAlignment
            .TryCreateSchedule(DateTime.UtcNow, "Mars/Olympus_Mons", out _)
            .Should().BeFalse();

        CalendarBillingAlignment
            .TryResolveFirstPeriod(DateTime.UtcNow, "Mars/Olympus_Mons", out _)
            .Should().BeFalse();
    }

    [Theory]
    // 7/31 of 10000 is 2258.06, and the nearest minor unit is 2258.
    [InlineData(10000, 7, 31, 2258)]
    // Exactly a half rounds away from zero rather than to even: half of 5 is 2.5, so 3.
    [InlineData(5, 1, 2, 3)]
    [InlineData(-5, 1, 2, -3)]
    [InlineData(10000, 31, 31, 10000)]
    [InlineData(10000, 0, 31, 0)]
    // A whole period, which is the default fraction, never scales anything.
    [InlineData(10000, 0, 0, 10000)]
    public void Proration_rounds_to_the_nearest_minor_unit_with_halves_away_from_zero(
        long amountMinor,
        int coveredDays,
        int totalDays,
        long expected) =>
        CalendarBillingAlignment.Prorate(amountMinor, coveredDays, totalDays)
            .Should().Be(expected);

    [Fact]
    public void A_default_fraction_is_a_whole_period()
    {
        var whole = default(BillingDayFraction);

        whole.IsPartial.Should().BeFalse();
        whole.Apply(8900).Should().Be(8900,
            "every call site that does not know about proration must keep charging full periods");
    }

    [Fact]
    public void A_snapshot_whose_cadence_contradicts_its_alignment_is_not_calendar_aligned()
    {
        CalendarBillingAlignment.IsCalendarAligned(new PriceSnapshot
        {
            BillingAlignment = BillingAlignment.CalendarMonth,
            Interval = BillingInterval.Month,
            IntervalCount = 3
        }).Should().BeFalse("a quarterly price has no single first of the month to renew on");

        CalendarBillingAlignment.IsCalendarAligned(new PriceSnapshot
        {
            BillingAlignment = BillingAlignment.CalendarMonth,
            Interval = BillingInterval.Month,
            IntervalCount = 1
        }).Should().BeTrue();

        CalendarBillingAlignment.IsCalendarAligned(null).Should().BeFalse();
    }

    private static void Resolve(DateTime nowUtc, out CalendarFirstPeriod period) =>
        CalendarBillingAlignment.TryResolveFirstPeriod(nowUtc, Zurich, out period)
            .Should().BeTrue();

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(
            new DateTime(year, month, day, 0, 0, 0),
            TimeZoneInfo.FindSystemTimeZoneById(Zurich));
}
