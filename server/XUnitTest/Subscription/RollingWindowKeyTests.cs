using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Addressing and summing a rolling sub-limit.
/// </summary>
/// <remarks>
/// Guards a rolling cap becoming a fixed one wearing its name, and guards the sum it produces
/// disagreeing with what was actually spent — the one property a "rolling" limit exists for is that
/// its answer changes every minute, not only on a boundary.
/// </remarks>
public sealed class RollingWindowKeyTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 14, 37, 22, DateTimeKind.Utc);

    [Fact]
    public void Two_different_spans_are_told_apart()
    {
        RollingWindowKey.Create(UsageWindow.Hour, 5)
            .Should().NotBe(
                RollingWindowKey.Create(UsageWindow.Hour, 6),
                because: "five hours and six hours are different rules, and one counter for both " +
                         "would enforce whichever the plan was last authored with");
    }

    [Fact]
    public void The_same_rule_always_addresses_the_same_counter()
    {
        RollingWindowKey.Create(UsageWindow.Hour, 5)
            .Should().Be(RollingWindowKey.Create(UsageWindow.Hour, 5),
                because: "a rolling window has no start of its own to be named after, so the rule " +
                         "is the only thing that can name its counter");
    }

    [Fact]
    public void A_bucket_never_collides_with_a_period_key()
    {
        var period = PeriodKey.Create(BillingInterval.Day, Now.Date);
        var rule = RollingWindowKey.Create(UsageWindow.Hour, 5);

        rule.Should().NotBe(period,
            because: "sharing an identity with the period's own counter would let a rolling cap " +
                     "count against the same balance it is meant to cap ahead of");
    }

    [Fact]
    public void A_use_is_bucketed_to_the_minute_it_happened_in()
    {
        RollingWindowKey.BucketFor(new DateTime(2026, 9, 16, 14, 37, 0, DateTimeKind.Utc))
            .Should().Be(RollingWindowKey.BucketFor(new DateTime(2026, 9, 16, 14, 37, 59, DateTimeKind.Utc)));
    }

    [Fact]
    public void The_next_minute_is_a_different_bucket()
    {
        RollingWindowKey.BucketFor(Now)
            .Should().NotBe(RollingWindowKey.BucketFor(Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(UsageWindow.Hour, 5, 5)]
    [InlineData(UsageWindow.Day, 2, 48)]
    public void The_span_is_the_count_of_windows(UsageWindow window, int count, int expectedHours)
    {
        RollingWindowKey.SpanOf(window, count).Should().Be(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public void Only_buckets_inside_the_span_are_summed()
    {
        var span = TimeSpan.FromHours(1);
        var buckets = new Dictionary<string, decimal>
        {
            [RollingWindowKey.BucketFor(Now)] = 10,
            [RollingWindowKey.BucketFor(Now.AddMinutes(-30))] = 20,
            // Just outside the span: spent nearly two hours ago against a one-hour rule.
            [RollingWindowKey.BucketFor(Now.AddHours(-2))] = 1_000,
        };

        RollingWindowKey.SumWithin(buckets, span, Now).Should().Be(30,
            because: "a bucket the window has already aged out of is not this window's spending, " +
                     "and summing it in would refuse somebody for usage the rule no longer covers");
    }

    [Fact]
    public void No_buckets_at_all_sums_to_nothing()
    {
        RollingWindowKey.SumWithin(null, TimeSpan.FromHours(1), Now).Should().Be(0);
    }

    /// <summary>
    /// The bucket a use lands in is kept whenever any part of it overlaps the span, which is the
    /// forgiving direction: the boundary bucket itself, exactly one span back, still counts.
    /// </summary>
    [Fact]
    public void A_bucket_exactly_on_the_boundary_still_counts()
    {
        var span = TimeSpan.FromHours(1);
        var boundary = RollingWindowKey.BucketFor(Now - span);

        RollingWindowKey.SumWithin(
                new Dictionary<string, decimal> { [boundary] = 5 }, span, Now)
            .Should().Be(5,
                because: "dropping the boundary bucket would refuse somebody for usage that " +
                         "happened exactly at the edge the rule promises to cover");
    }

    [Fact]
    public void Nothing_is_expired_before_two_spans_have_passed()
    {
        var span = TimeSpan.FromHours(1);
        var buckets = new Dictionary<string, decimal>
        {
            [RollingWindowKey.BucketFor(Now.AddMinutes(-90))] = 5,
        };

        RollingWindowKey.Expired(buckets, span, Now).Should().BeEmpty(
            because: "a bucket kept for a second span beyond the one being measured is what lets " +
                     "a slightly late write still land somewhere rather than reviving a pruned key");
    }

    [Fact]
    public void A_bucket_from_more_than_two_spans_ago_is_expired()
    {
        var span = TimeSpan.FromHours(1);
        var stale = RollingWindowKey.BucketFor(Now.AddHours(-3));
        var buckets = new Dictionary<string, decimal> { [stale] = 5 };

        RollingWindowKey.Expired(buckets, span, Now).Should().Contain(stale);
    }
}
