using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Naming the short window a sub-limit counts within.
/// </summary>
/// <remarks>
/// Guards a cap that enforces nothing. A sub-limit is a second counter on a second window, so if
/// its name could collide with the period's the two would count into one document — the "cap" would
/// simply be the period's own balance under another name, and nobody would see anything wrong.
/// <para>
/// The keyspaces are kept apart by construction rather than by a rule: a period key carries an
/// uppercase interval code and a window key a lowercase one, so no plan, however authored, can make
/// them equal.
/// </para>
/// </remarks>
public sealed class UsageWindowKeyTests
{
    private static readonly DateTime Instant =
        new(2026, 9, 16, 14, 37, 22, DateTimeKind.Utc);

    [Fact]
    public void A_window_key_can_never_equal_a_period_key()
    {
        var period = PeriodKey.Create(BillingInterval.Day, Instant.Date);
        var window = UsageWindowKey.Create(UsageWindow.Day, Instant);

        window.Should().NotBe(period,
            because: "a meter billed daily with a daily cap would otherwise count both into one " +
                     "counter, and the cap would enforce nothing at all");
    }

    [Fact]
    public void Everything_in_one_hour_shares_a_key()
    {
        UsageWindowKey.Create(UsageWindow.Hour, Instant)
            .Should().Be(UsageWindowKey.Create(
                UsageWindow.Hour, new DateTime(2026, 9, 16, 14, 2, 9, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_next_hour_starts_a_fresh_window()
    {
        UsageWindowKey.Create(UsageWindow.Hour, Instant)
            .Should().NotBe(UsageWindowKey.Create(UsageWindow.Hour, Instant.AddHours(1)),
                because: "an hourly cap that carried into the next hour would refuse a caller " +
                         "who had waited exactly as they were told to");
    }

    [Theory]
    [InlineData(14)] // Monday
    [InlineData(17)] // Thursday
    [InlineData(20)] // Sunday
    public void A_week_runs_from_monday(int dayOfMonth)
    {
        var within = new DateTime(2026, 9, dayOfMonth, 9, 0, 0, DateTimeKind.Utc);

        UsageWindowKey.StartOf(UsageWindow.Week, within)
            .Should().Be(new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc),
                because: "Sunday is the day this arithmetic gets wrong, and a week that reset " +
                         "mid-Sunday would hand everybody a second allowance");
    }

    [Fact]
    public void A_week_ends_seven_days_after_it_starts()
    {
        UsageWindowKey.EndOf(UsageWindow.Week, Instant)
            .Should().Be(UsageWindowKey.StartOf(UsageWindow.Week, Instant).AddDays(7));
    }

    [Fact]
    public void Different_lengths_of_window_are_told_apart()
    {
        var hour = UsageWindowKey.Create(UsageWindow.Hour, Instant);
        var day = UsageWindowKey.Create(UsageWindow.Day, Instant);
        var week = UsageWindowKey.Create(UsageWindow.Week, Instant);

        new[] { hour, day, week }.Should().OnlyHaveUniqueItems(
            because: "a meter capped by the hour and by the week counts two things, and one " +
                     "counter for both would enforce whichever was written last");
    }

    /// <summary>
    /// A count of one, the overload's default, addresses the same counter the three-argument
    /// call always has. Guards a migration: every meter authored before counts existed has to
    /// keep reading the counter it already wrote.
    /// </summary>
    [Fact]
    public void A_count_of_one_is_the_same_identity_as_no_count_at_all()
    {
        UsageWindowKey.Create(UsageWindow.Hour, Instant, count: 1)
            .Should().Be(UsageWindowKey.Create(UsageWindow.Hour, Instant));
    }

    /// <summary>
    /// A meter can hold a one-hour and a two-hour limit at once. At an even hour both blocks start
    /// at the same instant, and a key naming only the instant would have them spend one counter.
    /// </summary>
    [Fact]
    public void Blocks_of_different_lengths_starting_together_are_different_counters()
    {
        var evenHour = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

        UsageWindowKey.Create(UsageWindow.Hour, evenHour, count: 2)
            .Should().NotBe(UsageWindowKey.Create(UsageWindow.Hour, evenHour, count: 1));
    }

    [Fact]
    public void A_six_hour_block_starts_at_midnight_and_at_six_hour_marks()
    {
        var within = new DateTime(2026, 9, 16, 8, 30, 0, DateTimeKind.Utc);

        UsageWindowKey.StartOf(UsageWindow.Hour, within, count: 6)
            .Should().Be(new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc),
                because: "08:30 falls in the block that runs 06:00 to 12:00, and the block has " +
                         "to be named by where it starts, not by the instant that happened to " +
                         "land in it");
    }

    [Fact]
    public void Two_uses_in_the_same_six_hour_block_share_a_key()
    {
        UsageWindowKey.Create(UsageWindow.Hour, new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc), 6)
            .Should().Be(UsageWindowKey.Create(
                UsageWindow.Hour, new DateTime(2026, 9, 16, 11, 59, 0, DateTimeKind.Utc), 6),
                because: "a plan sold as ten every six hours has to count both uses into the " +
                         "same counter, or the cap only ever sees one hour at a time");
    }

    [Fact]
    public void The_next_six_hour_block_is_a_different_counter()
    {
        UsageWindowKey.Create(UsageWindow.Hour, new DateTime(2026, 9, 16, 5, 59, 0, DateTimeKind.Utc), 6)
            .Should().NotBe(UsageWindowKey.Create(
                UsageWindow.Hour, new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc), 6),
                because: "a block that never closed would eventually refuse every use once, " +
                         "however far apart, which is a fixed pace behaving like a lifetime cap");
    }

    [Fact]
    public void Six_hour_blocks_end_six_hours_after_they_start()
    {
        var start = UsageWindowKey.StartOf(UsageWindow.Hour, Instant, count: 6);

        UsageWindowKey.EndOf(UsageWindow.Hour, Instant, count: 6).Should().Be(start.AddHours(6));
    }

    /// <summary>
    /// A day has no enclosing unit for a multi-day block to divide, unlike an hour inside a day —
    /// so it tiles from a fixed, arbitrary epoch instead. The epoch has to be the same for every
    /// subscription, or two of them measuring "every three days" would disagree about where a
    /// block starts.
    /// </summary>
    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2027-06-01")]
    public void Multi_day_blocks_tile_consistently_from_a_fixed_epoch(string date)
    {
        var epoch = new DateTime(1969, 12, 29, 0, 0, 0, DateTimeKind.Utc);
        var instant = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

        var start = UsageWindowKey.StartOf(UsageWindow.Day, instant, count: 3);

        ((start - epoch).Days % 3).Should().Be(0,
            because: "a block boundary that did not land on a multiple of the count from the " +
                     "same fixed epoch every time would drift between subscriptions authored on " +
                     "different days");
    }

    /// <summary>
    /// The block a day was placed into actually contains that day. Divisibility by the count
    /// alone does not prove this: a block index left unscaled by the count still lands on a
    /// multiple of it, while naming a block nowhere near the instant that was supposed to be in
    /// it.
    /// </summary>
    [Fact]
    public void A_three_day_block_starts_no_later_than_the_instant_placed_in_it()
    {
        var instant = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

        var start = UsageWindowKey.StartOf(UsageWindow.Day, instant, count: 3);

        start.Should().BeOnOrBefore(instant).And.BeOnOrAfter(instant.AddDays(-3),
            because: "a block that does not contain the day it was computed from is not that " +
                     "day's block at all, whatever multiple of the count it happens to land on");
    }

    [Fact]
    public void Two_days_inside_the_same_three_day_block_share_a_key()
    {
        var first = UsageWindowKey.StartOf(UsageWindow.Day, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), count: 3);
        var second = UsageWindowKey.StartOf(UsageWindow.Day, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc).AddDays(2), count: 3);

        second.Should().Be(first,
            because: "a plan sold as so much every three days has to count both days into the " +
                     "same counter, or the cap only ever sees one day at a time");
    }

    [Fact]
    public void The_day_after_a_three_day_block_closes_is_a_different_block()
    {
        var withinBlock = UsageWindowKey.StartOf(
            UsageWindow.Day, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), count: 3);
        var afterBlock = UsageWindowKey.StartOf(
            UsageWindow.Day, new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc).AddDays(3), count: 3);

        afterBlock.Should().NotBe(withinBlock,
            because: "a block that never closed would eventually refuse every use once, " +
                     "however far apart, which is a fixed pace behaving like a lifetime cap");
    }

    [Fact]
    public void A_multi_week_block_still_starts_on_a_monday()
    {
        var start = UsageWindowKey.StartOf(UsageWindow.Week, Instant, count: 2);

        start.DayOfWeek.Should().Be(DayOfWeek.Monday,
            because: "every single-week plan already assumes a window starts on Monday, and a " +
                     "fortnightly one that landed mid-week would disagree with every reading a " +
                     "subscriber takes of their own single-week neighbour meters");
    }
}
