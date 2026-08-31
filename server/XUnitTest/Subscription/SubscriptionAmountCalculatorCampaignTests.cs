using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// How a campaign's own precedence meets a price's automatic and volume discounts -- ahead of the
/// plan's own <see cref="QuantityDiscountCombinationPolicy"/>, never negotiating with it.
/// </summary>
public sealed class SubscriptionAmountCalculatorCampaignTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReplaceBuiltIn_suppresses_the_automatic_discount_entirely()
    {
        // 8% automatic, 15% campaign: 15% is already larger, so this alone would not distinguish
        // ReplaceBuiltIn from BestDiscount. The point proven here is that the automatic discount is
        // reported as zero, not merely beaten.
        var subscription = NewSubscription(
            automaticBasisPoints: 800,
            campaignPercentBasisPoints: 1_500,
            precedence: CampaignPrecedence.ReplaceBuiltIn);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(0);
        charge.PromotionalDiscountMinor.Should().Be(1_500);
        charge.AmountMinor.Should().Be(8_500);
    }

    [Fact]
    public void ReplaceBuiltIn_wins_even_when_the_automatic_discount_is_the_larger_rate()
    {
        // The case BestDiscount and ReplaceBuiltIn actually disagree on: automatic is larger, and
        // a plain "biggest wins" comparison would have kept the automatic discount instead.
        var subscription = NewSubscription(
            automaticBasisPoints: 2_000,
            campaignPercentBasisPoints: 1_000,
            precedence: CampaignPrecedence.ReplaceBuiltIn);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(0);
        charge.AmountMinor.Should().Be(9_000); // 10% off 10,000, not 20%
    }

    [Fact]
    public void BestDiscount_still_picks_whichever_reduction_is_larger()
    {
        var subscription = NewSubscription(
            automaticBasisPoints: 2_000,
            campaignPercentBasisPoints: 1_000,
            precedence: CampaignPrecedence.BestDiscount);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(8_000); // the automatic 20% wins, campaign not consumed
        charge.DiscountApplied.Should().BeFalse();
    }

    [Fact]
    public void Stack_applies_the_campaign_on_top_of_the_automatic_discount()
    {
        var subscription = NewSubscription(
            automaticBasisPoints: 1_000,
            campaignPercentBasisPoints: 1_000,
            precedence: CampaignPrecedence.Stack);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // 10% off 10,000 = 9,000; a further 10% off 9,000 = 8,100 -- sequential, not 20% off the
        // original gross, which is the same non-compounding rule Stack already uses for an
        // ordinary coupon.
        charge.BuiltInDiscountMinor.Should().Be(1_000);
        charge.AmountMinor.Should().Be(8_100);
    }

    [Fact]
    public void A_plan_authored_as_built_in_discounts_only_does_not_silently_drop_a_campaign()
    {
        // QuantityDiscountCombinationPolicy.QuantityOnly means "no promotional code ever counts"
        // for an ordinary coupon -- a plan-level choice about coupons, made before this campaign
        // system existed and by an author who may never know a given campaign exists. A campaign's
        // own precedence must still take effect.
        var subscription = NewSubscription(
            automaticBasisPoints: 0,
            campaignPercentBasisPoints: 2_500,
            precedence: CampaignPrecedence.ReplaceBuiltIn);
        subscription.Plan.QuantityDiscountCombinationPolicy =
            QuantityDiscountCombinationPolicy.QuantityOnly;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(7_500);
        charge.DiscountApplied.Should().BeTrue();
    }

    [Fact]
    public void A_free_month_campaign_charges_nothing_regardless_of_the_plans_own_policy()
    {
        var subscription = NewSubscription(
            automaticBasisPoints: 500,
            campaignPercentBasisPoints: 10_000,
            precedence: CampaignPrecedence.ReplaceBuiltIn,
            kind: CampaignKind.FreeOpeningCalendarPeriod);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(0);
    }

    [Fact]
    public void A_legacy_standard_discount_keeps_the_plans_existing_policy()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { UnitAmountMinor = 10_000, AutomaticDiscountBasisPoints = 2_000 },
            Plan = new PlanSnapshot
            {
                QuantityDiscountCombinationPolicy = QuantityDiscountCombinationPolicy.Stack
            },
            Discount = new DiscountTerms
            {
                Kind = DiscountKind.Percent,
                PercentBasisPoints = 1_000,
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.Standard,
                    Precedence = CampaignPrecedence.ReplaceBuiltIn,
                    PrecedenceConfigured = false
                }
            }
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(7_200,
            "the stored precedence was previously ignored, so an old record must still Stack through the plan");
    }

    [Fact]
    public void A_standard_discount_with_explicit_precedence_overrides_the_plan_policy()
    {
        var subscription = new SubscriptionDetail
        {
            Price = new PriceSnapshot { UnitAmountMinor = 10_000, AutomaticDiscountBasisPoints = 2_000 },
            Plan = new PlanSnapshot
            {
                QuantityDiscountCombinationPolicy = QuantityDiscountCombinationPolicy.Stack
            },
            Discount = new DiscountTerms
            {
                Kind = DiscountKind.Percent,
                PercentBasisPoints = 1_000,
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.Standard,
                    Precedence = CampaignPrecedence.ReplaceBuiltIn,
                    PrecedenceConfigured = true
                }
            }
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(9_000,
            "the explicitly configured Standard code replaces the larger built-in discount");
        charge.BuiltInDiscountMinor.Should().Be(0);
    }

    private static SubscriptionDetail NewSubscription(
        int? automaticBasisPoints,
        int campaignPercentBasisPoints,
        CampaignPrecedence precedence,
        CampaignKind kind = CampaignKind.FirstAnnualPeriod) => new()
    {
        Price = new PriceSnapshot
        {
            UnitAmountMinor = 10_000,
            AutomaticDiscountBasisPoints = automaticBasisPoints
        },
        Plan = new PlanSnapshot(),
        Discount = new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = campaignPercentBasisPoints,
            Campaign = new CampaignTerms { Kind = kind, Precedence = precedence }
        }
    };
}
