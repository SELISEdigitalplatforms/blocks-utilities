using System.Globalization;
using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Billing boundaries, in the cases a calendar makes awkward.
/// </summary>
/// <remarks>
/// Every failure here is one a customer counts: a renewal on the wrong day, a quota that resets
/// twice, or a period that swallows an hour. None of them throw, so tests are the only place
/// they surface.
/// </remarks>
public sealed class BillingPeriodCalculatorTests
{
    private const string Zurich = "Europe/Zurich";

    [Fact]
    public void A_monthly_anchor_on_the_31st_returns_to_the_31st_after_february()
    {
        var schedule = MonthlyOn(31, new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        StartOfPeriodContaining(schedule, new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 1, 31),
                "mid-February is inside the period that opened on 31 January");

        StartOfPeriodContaining(schedule, new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 2, 28),
                "February has no 31st, so the boundary clamps to the last day it has");

        StartOfPeriodContaining(schedule, new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 3, 31),
                "February's clamp must not be written back, or every later period drifts early");

        StartOfPeriodContaining(schedule, new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 4, 30));

        StartOfPeriodContaining(schedule, new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 5, 31));
    }

    [Fact]
    public void A_february_29_anchor_falls_back_to_the_28th_in_a_non_leap_year()
    {
        var schedule = YearlyFrom(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc));

        StartOfPeriodContaining(schedule, new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2025, 2, 28));

        StartOfPeriodContaining(schedule, new DateTime(2028, 6, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2028, 2, 29), "the leap day returns when the year has one");
    }

    [Fact]
    public void A_boundary_inside_the_spring_forward_gap_moves_to_the_first_valid_instant()
    {
        // Zurich loses 02:00–03:00 on 29 March 2026, so a 02:30 boundary does not exist.
        var schedule = MonthlyAt(
            29,
            minutesFromMidnight: 150,
            anchorUtc: new DateTime(2026, 1, 29, 1, 30, 0, DateTimeKind.Utc));

        var boundary = StartOfPeriodContainingUtc(
            schedule,
            new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc));

        // 03:00 local on the transition day is 01:00 UTC.
        boundary.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void An_ambiguous_autumn_boundary_resolves_to_the_earlier_instant()
    {
        // Zurich repeats 02:00–03:00 on 25 October 2026, so 02:30 happens twice.
        var schedule = MonthlyAt(
            25,
            minutesFromMidnight: 150,
            anchorUtc: new DateTime(2026, 8, 25, 0, 30, 0, DateTimeKind.Utc));

        var boundary = StartOfPeriodContainingUtc(
            schedule,
            new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));

        // The first 02:30 is still on summer time, UTC+2.
        boundary.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_period_runs_from_one_boundary_to_the_next_without_gap_or_overlap()
    {
        var schedule = MonthlyOn(15, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        BillingPeriodCalculator
            .TryGetPeriod(schedule, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), out var first)
            .Should().BeTrue();

        BillingPeriodCalculator
            .TryGetPeriod(schedule, first.EndUtc, out var second)
            .Should().BeTrue();

        second.StartUtc.Should().Be(first.EndUtc, "the end of one period is the start of the next");
        second.Index.Should().Be(first.Index + 1);
    }

    [Fact]
    public void An_instant_on_a_boundary_belongs_to_the_period_it_opens()
    {
        var schedule = MonthlyOn(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        BillingPeriodCalculator
            .TryGetPeriod(schedule, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), out var period)
            .Should().BeTrue();

        period.Index.Should().Be(0);
    }

    [Theory]
    [InlineData(BillingInterval.Month, 3, "2026-01-01", "2026-02-15", "2026-01-01")]
    [InlineData(BillingInterval.Month, 3, "2026-01-01", "2026-04-02", "2026-04-01")]
    [InlineData(BillingInterval.Week, 2, "2026-01-01", "2026-01-10", "2026-01-01")]
    [InlineData(BillingInterval.Week, 2, "2026-01-01", "2026-01-16", "2026-01-15")]
    [InlineData(BillingInterval.Day, 10, "2026-01-01", "2026-01-09", "2026-01-01")]
    [InlineData(BillingInterval.Day, 10, "2026-01-01", "2026-01-11", "2026-01-11")]
    public void A_cadence_of_more_than_one_lands_on_the_right_boundary(
        BillingInterval interval,
        int intervalCount,
        string anchor,
        string instant,
        string expectedStart)
    {
        BillingPeriodCalculator.TryCreateSchedule(
            interval,
            intervalCount,
            Utc(anchor),
            "UTC",
            out var schedule).Should().BeTrue();

        StartOfPeriodContaining(schedule, Utc(instant))
            .Should().Be(Utc(expectedStart).Date);
    }

    /// <summary>
    /// Parses a date as an instant. <c>DateTime.Parse</c> alone yields a local kind, and
    /// converting that to UTC shifts the calendar day on any machine east or west of Greenwich —
    /// which would make these cases pass or fail depending on where they are run.
    /// </summary>
    private static DateTime Utc(string date) =>
        DateTime.SpecifyKind(
            DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeKind.Utc);

    [Fact]
    public void Adjacent_periods_of_a_multi_month_cadence_get_different_keys()
    {
        var schedule = QuarterlyFrom(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        BillingPeriodCalculator
            .TryGetPeriod(schedule, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), out var first)
            .Should().BeTrue();

        BillingPeriodCalculator
            .TryGetPeriod(schedule, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), out var second)
            .Should().BeTrue();

        second.Key.Should().NotBe(first.Key,
            "a key built from the calendar month rather than the period start would merge " +
            "two quarters into one counter");
    }

    [Fact]
    public void An_unknown_time_zone_is_reported_rather_than_thrown()
    {
        var schedule = MonthlyOn(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        schedule.TimeZoneId = "Middle/Earth";

        var resolved = BillingPeriodCalculator.TryGetPeriod(
            schedule,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            out _);

        resolved.Should().BeFalse();
    }

    [Fact]
    public void A_cadence_of_less_than_one_is_refused()
    {
        var schedule = MonthlyOn(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        schedule.IntervalCount = 0;

        BillingPeriodCalculator
            .TryGetPeriod(schedule, DateTime.UtcNow, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void A_schedule_takes_its_anchor_day_from_the_customers_calendar()
    {
        // Zurich is UTC+1 in January, so 23:30 on the 31st is 00:30 on the 1st there.
        BillingPeriodCalculator.TryCreateSchedule(
            BillingInterval.Month,
            1,
            new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc),
            Zurich,
            out var schedule).Should().BeTrue();

        schedule.AnchorDayOfMonth.Should().Be(1);
        schedule.AnchorMinutesFromMidnight.Should().Be(30);
    }

    private static DateTime StartOfPeriodContaining(BillingSchedule schedule, DateTime instantUtc)
    {
        BillingPeriodCalculator
            .TryGetPeriod(schedule, instantUtc, out var period)
            .Should().BeTrue();

        BillingLocalTime.TryFindTimeZone(schedule.TimeZoneId, out var timeZone)
            .Should().BeTrue();

        return BillingLocalTime.ToLocal(period.StartUtc, timeZone).Date;
    }

    private static DateTime StartOfPeriodContainingUtc(BillingSchedule schedule, DateTime instantUtc)
    {
        BillingPeriodCalculator
            .TryGetPeriod(schedule, instantUtc, out var period)
            .Should().BeTrue();

        return period.StartUtc;
    }

    private static BillingSchedule MonthlyOn(int day, DateTime anchorUtc) => new()
    {
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        AnchorInstantUtc = anchorUtc,
        TimeZoneId = "UTC",
        AnchorDayOfMonth = day,
        AnchorMinutesFromMidnight = 0
    };

    private static BillingSchedule MonthlyAt(
        int day,
        int minutesFromMidnight,
        DateTime anchorUtc) => new()
    {
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        AnchorInstantUtc = anchorUtc,
        TimeZoneId = Zurich,
        AnchorDayOfMonth = day,
        AnchorMinutesFromMidnight = minutesFromMidnight
    };

    private static BillingSchedule YearlyFrom(DateTime anchorUtc) => new()
    {
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        AnchorInstantUtc = anchorUtc,
        TimeZoneId = "UTC",
        AnchorDayOfMonth = anchorUtc.Day,
        AnchorMinutesFromMidnight = 0
    };

    private static BillingSchedule QuarterlyFrom(DateTime anchorUtc) => new()
    {
        Interval = BillingInterval.Month,
        IntervalCount = 3,
        AnchorInstantUtc = anchorUtc,
        TimeZoneId = "UTC",
        AnchorDayOfMonth = anchorUtc.Day,
        AnchorMinutesFromMidnight = 0
    };
}
