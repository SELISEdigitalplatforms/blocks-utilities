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
    public void A_campaign_can_require_card_setup_even_when_the_plan_and_trial_do_not()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Campaign = new CampaignTerms
            {
                Kind = CampaignKind.FreeOpeningCalendarPeriod,
                RequiresPaymentMethodUpfront = true
            }
        });

        SubscriptionAmountCalculator.RequiresCardSetup(subscription).Should().BeTrue();
    }

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

    [Fact]
    public void Tax_is_added_after_the_discount_not_before()
    {
        var subscription = NewSubscription(discount: new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500
        });
        subscription.Price.TaxRateBasisPoints = 1_000; // 10%

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // Discounted to 750, then 10% tax on 750 = 75. Not 10% of the original 1,000.
        charge.TaxAmountMinor.Should().Be(75);
        charge.AmountMinor.Should().Be(825);
    }

    [Fact]
    public void No_tax_rate_changes_nothing()
    {
        var subscription = NewSubscription(discount: null);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.TaxAmountMinor.Should().Be(0);
        charge.AmountMinor.Should().Be(1_000);
    }

    [Fact]
    public void Credit_is_consumed_against_the_tax_inclusive_total()
    {
        var subscription = NewSubscription(discount: null);
        subscription.Price.TaxRateBasisPoints = 1_000; // 10% of 1,000 = 100, total owed 1,100
        subscription.CreditBalanceMinor = 1_050;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.TaxAmountMinor.Should().Be(100);
        charge.CreditConsumedMinor.Should().Be(1_050);
        charge.AmountMinor.Should().Be(50);
    }

    [Fact]
    public void The_first_charge_is_also_taxed()
    {
        var subscription = NewSubscription(discount: null);
        subscription.Price.TaxRateBasisPoints = 1_000;

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription).Should().Be(1_100);
    }

    private static SubscriptionDetail NewSubscription(DiscountTerms? discount) => new()
    {
        Price = new PriceSnapshot { UnitAmountMinor = 1_000 },
        Discount = discount
    };
}
