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

        var outcome = Calculate(
            subscription, targetPrice, [], PeriodStart);

        outcome.ChargeMinor.Should().Be(1_000);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public void An_upgrade_right_at_period_end_charges_almost_nothing()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        var targetPrice = NewPrice(2_000);

        var outcome = Calculate(
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

        var outcome = Calculate(
            subscription, targetPrice, [], halfway);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_existing_credit_balance_is_applied_before_any_new_charge()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000, creditBalanceMinor: 1_000);
        var targetPrice = NewPrice(2_000);

        var outcome = Calculate(
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

        var outcome = Calculate(
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

        var outcome = Calculate(
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

        var outcome = Calculate(
            subscription, targetPrice, [], PeriodStart.AddDays(10));

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    [Fact]
    public void A_taxed_upgrade_nets_the_tax_inclusive_amounts()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = 1_000; // 10%
        var targetPrice = NewPrice(2_000);
        targetPrice.TaxRateBasisPoints = 1_000;

        var outcome = Calculate(
            subscription, targetPrice, [], PeriodStart);

        // Old side: 1,000 + 10% = 1,100. New side: 2,000 + 10% = 2,200. Delta = 1,100.
        outcome.ChargeMinor.Should().Be(1_100);
    }

    [Fact]
    public void A_plan_change_between_differently_taxed_prices_taxes_each_side_at_its_own_rate()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = null; // old price is untaxed
        var targetPrice = NewPrice(1_000);
        targetPrice.TaxRateBasisPoints = 2_000; // new price carries 20% tax

        var outcome = Calculate(
            subscription, targetPrice, [], PeriodStart);

        // Old side stays 1,000 (no tax). New side is 1,000 + 20% = 1,200. Delta = 200.
        outcome.ChargeMinor.Should().Be(200);
    }

    [Fact]
    public void A_change_from_an_inclusive_price_to_an_exclusive_one_compares_like_with_like()
    {
        // The failure this rules out: netting the two configured amounts before settling the tax.
        // Both prices read 1,000, so a naive delta is zero — but one of those thousands already
        // contains tax and the other is about to have tax added, and the subscriber owes the
        // difference.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = 2_000;
        subscription.Price.TaxMode = TaxMode.Inclusive;

        var targetPrice = NewPrice(1_000);
        targetPrice.TaxRateBasisPoints = 2_000;
        targetPrice.TaxMode = TaxMode.Exclusive;

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        // Old side is worth 1,000 (tax already inside). New side costs 1,200. Delta = 200.
        outcome.ChargeMinor.Should().Be(200);
    }

    [Fact]
    public void A_change_from_an_exclusive_price_to_an_inclusive_one_credits_the_difference()
    {
        // The same arithmetic in the direction that produces a credit rather than a charge, because
        // a downgrade that silently charged instead would be the more expensive bug.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = 2_000;
        subscription.Price.TaxMode = TaxMode.Exclusive;

        var targetPrice = NewPrice(1_000);
        targetPrice.TaxRateBasisPoints = 2_000;
        targetPrice.TaxMode = TaxMode.Inclusive;

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(200);
    }

    [Fact]
    public void An_inclusive_monthly_to_yearly_change_prorates_on_what_the_subscriber_actually_pays()
    {
        // Monthly to annual, both inclusive: the amounts being prorated are the amounts on the
        // pricing page, so the figures a subscriber can check are the figures used.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Price.TaxMode = TaxMode.Inclusive;

        var targetPrice = NewPrice(10_000);
        targetPrice.TaxRateBasisPoints = 770;
        targetPrice.TaxMode = TaxMode.Inclusive;

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        // Nothing is added to either side, so the delta is the plain difference between the two
        // configured amounts across a full period.
        outcome.ChargeMinor.Should().Be(9_000);
    }

    [Fact]
    public void A_legacy_untyped_rate_prorates_as_exclusive_on_both_sides()
    {
        // A subscription sold before modes existed, changing to a price authored the same way.
        // Neither side may move, because the mode was never a decision anybody made.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.TaxRateBasisPoints = 1_000;
        subscription.Price.TaxMode = null;

        var targetPrice = NewPrice(2_000);
        targetPrice.TaxRateBasisPoints = 1_000;
        targetPrice.TaxMode = null;

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        outcome.ChargeMinor.Should().Be(1_100, "the same answer as before modes existed");
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

    private static ProrationOutcome Calculate(
        SubscriptionDetail subscription,
        PriceSnapshot targetPrice,
        IReadOnlyList<SubscriptionQuantityItem> quantities,
        DateTime nowUtc) => SubscriptionProrationCalculator.Calculate(
            subscription,
            // The same plan on both sides: these cases vary price and quantity, not the plan.
            subscription.Plan,
            targetPrice,
            quantities,
            nowUtc,
            PeriodStart,
            PeriodEnd);
}
