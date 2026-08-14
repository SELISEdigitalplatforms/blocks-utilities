using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// What a period costs, and whether a discount is still the reason it costs less.
/// </summary>
public sealed class SubscriptionAmountCalculatorTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_discount_with_no_bound_never_expires()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500
        });

        subscription.DiscountPeriodsApplied = 50;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.DiscountApplied.Should().BeTrue();
        charge.AmountMinor.Should().Be(750);
    }

    [Fact]
    public void A_discount_stops_after_its_duration_in_periods()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500,
            DurationPeriods = 3
        });

        subscription.DiscountPeriodsApplied = 3;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.DiscountApplied.Should().BeFalse();
        charge.AmountMinor.Should().Be(1_000);
    }

    [Fact]
    public void A_discount_still_applies_on_its_last_eligible_period()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500,
            DurationPeriods = 3
        });

        subscription.DiscountPeriodsApplied = 2;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.DiscountApplied.Should().BeTrue();
        charge.AmountMinor.Should().Be(750);
    }

    [Fact]
    public void A_discount_stops_after_its_expiry_even_with_periods_remaining()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500,
            DurationPeriods = 12,
            ExpiresAtUtc = Now.AddDays(-1)
        });

        subscription.DiscountPeriodsApplied = 1;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.DiscountApplied.Should().BeFalse();
        charge.AmountMinor.Should().Be(1_000);
    }

    [Fact]
    public void No_discount_is_a_full_charge()
    {
        var subscription = NewSubscription(discount: null);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.DiscountApplied.Should().BeFalse();
        charge.AmountMinor.Should().Be(1_000);
    }

    private static SubscriptionDetail NewSubscription(DiscountTerms? discount) => new()
    {
        Price = new PriceSnapshot { UnitAmountMinor = 1_000 },
        Discount = discount
    };
}
