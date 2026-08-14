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

    [Fact]
    public void A_credit_balance_reduces_the_charge_and_reports_what_it_consumed()
    {
        var subscription = NewSubscription(discount: null);
        subscription.CreditBalanceMinor = 400;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(600);
        charge.CreditConsumedMinor.Should().Be(400);
    }

    [Fact]
    public void A_credit_larger_than_the_period_amount_never_produces_a_negative_charge()
    {
        var subscription = NewSubscription(discount: null);
        subscription.CreditBalanceMinor = 5_000;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(0);
        charge.CreditConsumedMinor.Should().Be(1_000,
            "only what the period actually costs is consumed, the rest stays banked");
    }

    private static SubscriptionDetail NewSubscription(DiscountTerms? discount) => new()
    {
        Price = new PriceSnapshot { UnitAmountMinor = 1_000 },
        Discount = discount
    };
}
