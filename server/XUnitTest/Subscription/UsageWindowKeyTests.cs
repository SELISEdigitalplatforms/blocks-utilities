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
}
