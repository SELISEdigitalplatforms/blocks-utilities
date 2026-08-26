using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Trial-end boundaries, in the cases a calendar and a clock change make awkward.
/// </summary>
/// <remarks>
/// Every failure here is a trial that ends on the wrong day or the wrong instant, silently —
/// none of this throws, so tests are the only place it surfaces. Zurich is used throughout
/// because it moves the clock on the same two nights <see cref="BillingPeriodCalculatorTests"/>
/// already exercises for billing boundaries, so the expected instants there double as a
/// cross-check.
/// </remarks>
public sealed class TrialDurationResolverTests
{
    private static readonly TimeZoneInfo Zurich = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    [Fact]
    public void Days_mode_ends_exactly_the_count_times_24_hours_later_regardless_of_time_zone()
    {
        var startUtc = new DateTime(2026, 8, 25, 13, 47, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(startUtc, Zurich, TrialDurationKind.Days, 14);

        endUtc.Should().Be(startUtc.AddDays(14),
            "a day-based trial is a fixed span, not a calendar boundary");
    }

    [Fact]
    public void End_of_calendar_month_ends_at_local_midnight_on_the_first_of_the_next_month()
    {
        // 25 August 2026, 10:00 local (Zurich is CEST, UTC+2, in August).
        var startUtc = new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.EndOfCalendarMonth, null);

        // 1 September local midnight, still CEST.
        endUtc.Should().Be(new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_late_signup_still_ends_at_the_first_of_the_next_month()
    {
        // 31 August 2026, 23:00 local — a trial lasting barely over an hour.
        var startUtc = new DateTime(2026, 8, 31, 21, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.EndOfCalendarMonth, null);

        endUtc.Should().Be(new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc),
            "an August 31 signup can have a trial lasting only until September 1");
    }

    [Fact]
    public void Anniversary_months_ends_the_same_local_time_n_months_later()
    {
        // 25 August 2026, 10:00 local (CEST, UTC+2).
        var startUtc = new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 1);

        // 25 September 2026, 10:00 local — still CEST, so the same UTC offset applies.
        endUtc.Should().Be(new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anniversary_months_crosses_a_year_boundary()
    {
        // 15 December 2026, 09:00 local (CET, UTC+1).
        var startUtc = new DateTime(2026, 12, 15, 8, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 2);

        // 15 February 2027, 09:00 local — still CET.
        endUtc.Should().Be(new DateTime(2027, 2, 15, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anniversary_months_clamps_to_the_target_month_s_last_day()
    {
        // 31 January 2026, 09:00 local (CET, UTC+1). 2026 is not a leap year.
        var startUtc = new DateTime(2026, 1, 31, 8, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 1);

        // February has no 31st, so the boundary clamps to the 28th — and the clamp is not
        // written back, so a plan resolved from a different start is unaffected.
        endUtc.Should().Be(new DateTime(2026, 2, 28, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anniversary_months_clamps_to_the_29th_in_a_leap_year()
    {
        // 31 January 2024, 09:00 local (CET, UTC+1). 2024 is a leap year.
        var startUtc = new DateTime(2024, 1, 31, 8, 0, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 1);

        endUtc.Should().Be(new DateTime(2024, 2, 29, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anniversary_months_landing_in_the_spring_forward_gap_moves_to_the_first_valid_instant()
    {
        // 29 January 2026, 02:30 local (CET) — two months later is 29 March 2026, which loses
        // 02:00-03:00 to the spring-forward transition, so 02:30 does not exist that day.
        var startUtc = new DateTime(2026, 1, 29, 1, 30, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 2);

        // 03:00 local on the transition day is 01:00 UTC — the same instant
        // BillingPeriodCalculatorTests expects for an identical gap boundary.
        endUtc.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anniversary_months_landing_in_an_ambiguous_autumn_hour_resolves_to_the_earlier_instant()
    {
        // 25 September 2026, 02:30 local (CEST) — one month later is 25 October 2026, which
        // repeats 02:00-03:00 for the autumn transition, so 02:30 happens twice.
        var startUtc = new DateTime(2026, 9, 25, 0, 30, 0, DateTimeKind.Utc);

        var endUtc = TrialDurationResolver.ResolveEndUtc(
            startUtc, Zurich, TrialDurationKind.AnniversaryMonths, 1);

        // The earlier of the two instants — still CEST (UTC+2) — matching how a renewal boundary
        // resolves the same ambiguity.
        endUtc.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
    }
}
