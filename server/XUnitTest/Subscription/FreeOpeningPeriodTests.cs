using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Whether a free-opening-period campaign is still running.
/// </summary>
/// <remarks>
/// Guards against a subscriber who has renewed onto a paid month staying locked out of plan and
/// quantity changes, and capped by the campaign's entitlement, for as long as they stay
/// subscribed: the campaign discount outlives its free month, and CurrentPeriodEndUtc moves on at
/// every renewal.
/// </remarks>
public sealed class FreeOpeningPeriodTests
{
    private static readonly DateTime SignupUtc = new(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_offer_is_in_force_during_the_period_the_code_was_redeemed_in()
    {
        var subscription = Subscription(
            SignupUtc, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), SignupUtc);

        FreeOpeningPeriod.IsInForce(subscription, SignupUtc.AddDays(3))
            .Should().BeTrue("the subscriber is still inside the free month they were promised");
    }

    [Fact]
    public void The_offer_has_ended_once_renewal_has_opened_a_paid_month()
    {
        var subscription = Subscription(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            SignupUtc);

        FreeOpeningPeriod.IsInForce(subscription, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeFalse("a renewed subscriber paying full price must be able to change plan");
    }

    [Fact]
    public void The_offer_has_ended_once_the_opening_period_has_passed()
    {
        var subscription = Subscription(
            SignupUtc, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), SignupUtc);

        FreeOpeningPeriod.IsInForce(subscription, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeFalse("the lock lifts at the boundary even before the renewal sweep runs");
    }

    [Fact]
    public void A_standard_discount_is_never_a_free_opening_offer()
    {
        var subscription = Subscription(
            SignupUtc, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), SignupUtc);
        subscription.Discount!.Campaign.Kind = CampaignKind.Standard;

        FreeOpeningPeriod.IsInForce(subscription, SignupUtc.AddDays(3))
            .Should().BeFalse("an ordinary discount never locks plan or quantity changes");
    }

    private static SubscriptionDetail Subscription(
        DateTime periodStartUtc, DateTime periodEndUtc, DateTime? redeemedAtUtc) => new()
    {
        CurrentPeriodStartUtc = periodStartUtc,
        CurrentPeriodEndUtc = periodEndUtc,
        Discount = new DiscountTerms
        {
            Code = "free1",
            RedeemedAtUtc = redeemedAtUtc,
            Campaign = new CampaignTerms { Kind = CampaignKind.FreeOpeningCalendarPeriod }
        }
    };
}
