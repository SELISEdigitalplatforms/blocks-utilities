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

    /// <summary>
    /// A settlement worth less than what it replaces charges nothing and banks nothing.
    /// </summary>
    /// <remarks>
    /// This used to bank the difference as credit. It no longer does: the subscriber keeps the
    /// period they already paid for, so there is no unused time to hand back, and creating credit
    /// for it would be a refund under another name. The balance is left exactly where it was.
    /// </remarks>
    [Fact]
    public void A_settlement_worth_less_than_what_it_replaces_banks_nothing()
    {
        var subscription = NewSubscription(oldAmountMinor: 2_000);
        var targetPrice = NewPrice(1_000);
        var halfway = PeriodStart.AddDays(15);

        var outcome = Calculate(
            subscription, targetPrice, [], halfway);

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    /// <summary>
    /// The same, with a balance already on the account: it survives untouched rather than growing.
    /// </summary>
    /// <remarks>
    /// The distinction the clamp exists for. Credit already banked is real money the subscriber is
    /// owed and must persist; what must not happen is this settlement adding to it.
    /// </remarks>
    [Fact]
    public void An_existing_balance_is_preserved_rather_than_grown_by_a_cheaper_settlement()
    {
        var subscription = NewSubscription(oldAmountMinor: 2_000, creditBalanceMinor: 750);
        var targetPrice = NewPrice(1_000);

        var outcome = Calculate(
            subscription, targetPrice, [], PeriodStart.AddDays(15));

        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(750);
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

        // The tax modes still settle independently — the point of the test — but the 200 the
        // difference comes to is no longer banked: nothing charges, and nothing is handed back.
        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(0);
        outcome.Breakdown.NetSettlementMinor.Should().Be(-200);
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

    [Fact]
    public void A_settlement_reports_both_sides_it_was_subtracted_from()
    {
        // The figure a subscriber queries is a remainder, and a remainder cannot explain itself. Both
        // priced periods and both prorated values are reported so an invoice can show the subtraction
        // rather than only its answer.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.Price.AutomaticDiscountBasisPoints = 1_000;
        subscription.Price.TaxRateBasisPoints = 1_000;
        subscription.Price.TaxMode = TaxMode.Exclusive;

        var targetPrice = NewPrice(2_000);
        targetPrice.AutomaticDiscountBasisPoints = 800;
        targetPrice.TaxRateBasisPoints = 1_000;
        targetPrice.TaxMode = TaxMode.Exclusive;

        var halfway = PeriodStart.AddDays(15);
        var outcome = Calculate(subscription, targetPrice, [], halfway);

        var outgoing = outcome.Breakdown.Outgoing;
        var target = outcome.Breakdown.Target;

        // Outgoing: 1,000 less 10% is 900, plus 10% tax is 990 for the period.
        outgoing.GrossAmountMinor.Should().Be(1_000);
        outgoing.BuiltInDiscountMinor.Should().Be(100);
        outgoing.PromotionalDiscountMinor.Should().Be(0);
        outgoing.TaxAmountMinor.Should().Be(90);
        outgoing.PeriodTotalMinor.Should().Be(990);

        // Target: 2,000 less 8% is 1,840, plus 10% tax is 2,024.
        target.GrossAmountMinor.Should().Be(2_000);
        target.BuiltInDiscountMinor.Should().Be(160);
        target.TaxAmountMinor.Should().Be(184);
        target.PeriodTotalMinor.Should().Be(2_024);

        // Each side's own rate reaches its own proration, and the charge is the difference between
        // the two prorated values — not a percentage of anything.
        outcome.ChargeMinor.Should().Be(
            target.ProratedValueMinor - outgoing.ProratedValueMinor);
        outcome.Breakdown.NetSettlementMinor.Should().Be(outcome.ChargeMinor);
    }

    [Fact]
    public void A_settlement_reports_the_credit_it_actually_spent()
    {
        var subscription = NewSubscription(oldAmountMinor: 1_000, creditBalanceMinor: 300);
        var targetPrice = NewPrice(2_000);

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        outcome.Breakdown.CreditConsumedMinor.Should().Be(300);
        outcome.ChargeMinor.Should().Be(700, "1,000 of difference less the 300 banked");
        outcome.Breakdown.NetSettlementMinor.Should().Be(700);
    }

    [Fact]
    public void A_downgrade_spends_no_credit_and_says_so()
    {
        // The delta is negative: nothing is charged, and the balance neither shrinks nor grows.
        // Reporting the whole balance as "consumed" would describe money that was not spent.
        var subscription = NewSubscription(oldAmountMinor: 2_000, creditBalanceMinor: 300);
        var targetPrice = NewPrice(1_000);

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart.AddDays(15));

        outcome.ChargeMinor.Should().Be(0);
        outcome.Breakdown.CreditConsumedMinor.Should().Be(0);
        outcome.Breakdown.NetSettlementMinor.Should().BeNegative();
        outcome.NewCreditBalanceMinor.Should().Be(300);
    }

    [Fact]
    public void A_promotion_appears_on_the_side_it_reduced()
    {
        var subscription = NewSubscription(
            oldAmountMinor: 1_000,
            discount: new DiscountTerms
            {
                Code = "half",
                Kind = DiscountKind.Percent,
                PercentBasisPoints = 5_000
            });
        var targetPrice = NewPrice(2_000);

        var outcome = Calculate(subscription, targetPrice, [], PeriodStart);

        // The subscriber's own code applies to both sides — it belongs to them, not to a price.
        outcome.Breakdown.Outgoing.PromotionalDiscountMinor.Should().Be(500);
        outcome.Breakdown.Target.PromotionalDiscountMinor.Should().Be(1_000);
    }

    [Fact]
    public void A_malformed_period_reports_no_breakdown_rather_than_zeroes()
    {
        // Nothing was prorated, so there are no two sides. Zeroes would claim both periods were free.
        var subscription = NewSubscription(oldAmountMinor: 1_000);
        subscription.CurrentPeriodEndUtc = subscription.CurrentPeriodStartUtc;

        var outcome = Calculate(subscription, NewPrice(2_000), [], PeriodStart);

        outcome.Breakdown.Should().Be(default(ProrationBreakdown));
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
