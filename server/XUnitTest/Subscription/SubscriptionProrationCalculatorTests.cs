using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>What a mid-period plan change costs, or credits, right now.</summary>
public sealed class SubscriptionProrationCalculatorTests
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_upgrade_right_at_period_start_charges_almost_the_full_difference()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        var targetPrice = NewPrice(2_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodStart);

        outcome.ChargeMinor.Should().Be(1_000);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public void An_upgrade_right_at_period_end_charges_almost_nothing()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        var targetPrice = NewPrice(2_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodEnd);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public void A_downgrade_halfway_through_the_period_banks_a_credit_instead_of_charging()
    {
        var subscription = NewSubscription(oldAmountMinor: 2_000);
        var targetPrice = NewPrice(1_000);
        var halfway = PeriodStart.AddDays(15);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], halfway);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_existing_credit_balance_is_applied_before_any_new_charge()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000, creditBalanceMinor: 1_000);
        var targetPrice = NewPrice(2_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodStart);

        // The full 1,000 difference is covered by the existing 1,000 credit.
        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public void A_credit_larger_than_the_upgrade_leaves_the_remainder_banked()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000, creditBalanceMinor: 5_000);
        var targetPrice = NewPrice(2_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodStart);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(4_000);
    }

    [Fact]
    public void A_discount_reduces_both_sides_of_the_comparison_identically()
    {
        var subscription = NewSubscription(
            oldAmountMinor: 1_000,
            discount: new DiscountTerms { Kind = DiscountKind.Percent, PercentBasisPoints = 5_000 });
        var targetPrice = NewPrice(2_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodStart);

        // Without the discount this would be 1,000 (2,000 - 1,000); halved on both sides it is
        // still exactly half the undiscounted difference.
        outcome.ChargeMinor.Should().Be(500);
    }

    [Fact]
    public void No_change_in_price_neither_charges_nor_credits()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        var targetPrice = NewPrice(1_000);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription, targetPrice, [], PeriodStart.AddDays(10));

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    private static SubscriptionDetail NewSubscription(
        long oldAmountMinor,
        long creditBalanceMinor = 0,
        DiscountTerms? discount = null) => new()
    {
        CurrentPeriodStartUtc = PeriodStart,
        CurrentPeriodEndUtc = PeriodEnd,
        Price = NewPrice(oldAmountMinor),
        QuantityItems = [],
        Discount = discount,
        CreditBalanceMinor = creditBalanceMinor
    };

    private static PriceSnapshot NewPrice(long unitAmountMinor) =>
        new() { UnitAmountMinor = unitAmountMinor };
}
