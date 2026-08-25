using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using System.Text.Json;

namespace XUnitTest.Subscription;

/// <summary>
/// What a price's own automatic discount does to the money.
/// </summary>
/// <remarks>
/// The case this exists for: one plan, a monthly price at full rate and a yearly price at 8% off,
/// with no code involved. Everything here is about which reductions apply, in what order, and what
/// happens when more than one of them wants the same charge.
/// <para>
/// The rates are 8% for the cadence and 5% for the volume throughout, because 8 + 5 = 13 is exactly
/// the arithmetic the two combinations disagree about — additive gives 13%, compounding would give
/// 12.6%, and "best" gives 8%. Three different answers to the same pair of numbers is the whole
/// reason the choice is stored rather than assumed.
/// </para>
/// </remarks>
public sealed class AutomaticPriceDiscountTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_price_with_an_automatic_discount_charges_below_its_configured_amount()
    {
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(100_000, automaticBasisPoints: 800),
            Now);

        // The yearly price: CHF 1,000.00 authored, 8% off, nothing typed by the subscriber.
        charge.GrossAmountMinor.Should().Be(100_000);
        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.PromotionalDiscountMinor.Should().Be(0);
        charge.AmountMinor.Should().Be(92_000);
    }

    [Fact]
    public void Two_prices_under_one_plan_can_discount_differently()
    {
        // The requirement in one test. Same plan, same subscriber, two prices — and the monthly one
        // must not inherit the yearly one's offer.
        var monthly = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(10_000, automaticBasisPoints: null), Now);
        var yearly = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(100_000, automaticBasisPoints: 800), Now);

        monthly.AmountMinor.Should().Be(10_000);
        monthly.BuiltInDiscountMinor.Should().Be(0);
        yearly.AmountMinor.Should().Be(92_000);
    }

    [Fact]
    public void Additive_adds_the_two_rates_and_applies_them_once()
    {
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            SubscribedWithBand(
                unitAmountMinor: 10_000,
                quantity: 10,
                bandBasisPoints: 500,
                automaticBasisPoints: 800,
                combination: AutomaticDiscountCombination.Additive),
            Now);

        // 100,000 gross, 13% off — exactly 13,000, not the 12,600 that applying 8% and then 5% of
        // what is left would produce.
        charge.GrossAmountMinor.Should().Be(100_000);
        charge.BuiltInDiscountMinor.Should().Be(13_000);
        charge.AmountMinor.Should().Be(87_000);
    }

    [Fact]
    public void Best_discount_applies_only_the_larger_of_the_two()
    {
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            SubscribedWithBand(
                unitAmountMinor: 10_000,
                quantity: 10,
                bandBasisPoints: 500,
                automaticBasisPoints: 800,
                combination: AutomaticDiscountCombination.BestDiscount),
            Now);

        // 8% beats 5%, and the 5% is not also given.
        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.AmountMinor.Should().Be(92_000);
    }

    [Fact]
    public void Best_discount_lets_the_band_win_when_the_band_is_larger()
    {
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            SubscribedWithBand(
                unitAmountMinor: 10_000,
                quantity: 10,
                bandBasisPoints: 1_500,
                automaticBasisPoints: 800,
                combination: AutomaticDiscountCombination.BestDiscount),
            Now);

        charge.BuiltInDiscountMinor.Should().Be(15_000);
        charge.AmountMinor.Should().Be(85_000);
    }

    [Fact]
    public void An_additive_pair_can_never_take_more_than_everything()
    {
        // Two generous rates summing past 100%. A charge must not arrive negative, and the cap is
        // the only thing standing between a mis-authored pair and a refund by arithmetic.
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            SubscribedWithBand(
                unitAmountMinor: 10_000,
                quantity: 1,
                bandBasisPoints: 6_000,
                automaticBasisPoints: 6_000,
                combination: AutomaticDiscountCombination.Additive),
            Now);

        charge.BuiltInDiscountMinor.Should().Be(10_000);
        charge.AmountMinor.Should().Be(0);
    }

    [Fact]
    public void A_missing_combination_reads_as_best_discount()
    {
        // What a caller that has never heard of this field sends, and what every price stored before
        // it existed carries. It has to be the conservative answer, not the generous one.
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            SubscribedWithBand(
                unitAmountMinor: 10_000,
                quantity: 10,
                bandBasisPoints: 500,
                automaticBasisPoints: 800,
                combination: null),
            Now);

        charge.BuiltInDiscountMinor.Should().Be(8_000, "the larger of the two, never their sum");
    }

    [Fact]
    public void A_price_without_an_automatic_discount_is_banded_exactly_as_before()
    {
        // The case almost every stored price is in. Its arithmetic must be untouched to the minor
        // unit — a band that produced 4,999 before must not start producing 5,000.
        var band = QuantityDiscountCalculator.Resolve(
            [new QuantityDiscountTier { MinimumQuantity = 1, DiscountBasisPoints = 500 }],
            unitAmountMinor: 3_333,
            quantity: 3);

        var builtIn = BuiltInDiscountCalculator.Resolve(
            9_999,
            band,
            automaticBasisPoints: null,
            combination: null);

        builtIn.DiscountAmountMinor.Should().Be(band.DiscountAmountMinor);
        builtIn.SubtotalMinor.Should().Be(band.SubtotalMinor);
        builtIn.EffectiveBasisPoints.Should().Be(500);
    }

    [Fact]
    public void Nothing_is_taken_off_a_charge_of_nothing()
    {
        var builtIn = BuiltInDiscountCalculator.Resolve(
            0,
            new QuantityDiscountOutcome(null, 0, 0, 0, 0),
            automaticBasisPoints: 800,
            combination: AutomaticDiscountCombination.Additive);

        builtIn.DiscountAmountMinor.Should().Be(0);
        builtIn.SubtotalMinor.Should().Be(0);
    }

    [Fact]
    public void A_promotion_larger_than_the_built_in_discount_replaces_it()
    {
        // The plan's own policy, unchanged by any of this: BestDiscount compares the code against
        // whatever the price and band already produced.
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Discount = new DiscountTerms
        {
            Code = "launch25",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_500
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(0, "only one of the two applies");
        charge.PromotionalDiscountMinor.Should().Be(25_000);
        charge.AmountMinor.Should().Be(75_000);
        charge.DiscountApplied.Should().BeTrue("the code reduced the charge, so it is consumed");
    }

    [Fact]
    public void A_promotion_smaller_than_the_built_in_discount_is_not_consumed()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Discount = new DiscountTerms
        {
            Code = "small",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 200,
            DurationPeriods = 3
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.PromotionalDiscountMinor.Should().Be(0);
        charge.AmountMinor.Should().Be(92_000);
        charge.DiscountApplied.Should().BeFalse(
            "three months of 2% off must not expire on periods where it reduced nothing");
    }

    [Fact]
    public void Stack_applies_the_promotion_to_what_the_built_in_discount_left()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Plan.QuantityDiscountCombinationPolicy =
            QuantityDiscountCombinationPolicy.Stack;
        subscription.Discount = new DiscountTerms
        {
            Code = "extra10",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // 8% off 100,000 is 92,000; 10% of that is 9,200.
        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.PromotionalDiscountMinor.Should().Be(9_200);
        charge.AmountMinor.Should().Be(82_800);
    }

    [Fact]
    public void Quantity_only_suppresses_the_code_and_keeps_the_built_in_discount()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Plan.QuantityDiscountCombinationPolicy =
            QuantityDiscountCombinationPolicy.QuantityOnly;
        subscription.Discount = new DiscountTerms
        {
            Code = "ignored",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 5_000
        };

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.PromotionalDiscountMinor.Should().Be(0);
        charge.AmountMinor.Should().Be(92_000);
    }

    [Fact]
    public void Tax_is_calculated_after_the_automatic_discount()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Price.TaxMode = TaxMode.Exclusive;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // 7.7% of 92,000, not of 100,000. Taxing the gross would charge tax on money nobody paid.
        charge.NetAmountMinor.Should().Be(92_000);
        charge.TaxAmountMinor.Should().Be(7_084);
        charge.AmountMinor.Should().Be(99_084);
    }

    [Fact]
    public void An_inclusive_price_still_charges_its_amount_less_the_discount()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Price.TaxMode = TaxMode.Inclusive;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // The configured amount is what a customer pays before the discount, so 8% off it is what
        // they pay after — with the tax found inside the reduced figure, not the original.
        charge.AmountMinor.Should().Be(92_000);
        charge.TaxAmountMinor.Should().Be(6_578, "92,000 × 770 / 10,770");
        charge.NetAmountMinor.Should().Be(85_422);
    }

    [Fact]
    public void Credit_is_spent_last_and_does_not_change_what_was_discounted()
    {
        var subscription = Subscribed(100_000, automaticBasisPoints: 800);
        subscription.CreditBalanceMinor = 20_000;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.BuiltInDiscountMinor.Should().Be(8_000);
        charge.CreditConsumedMinor.Should().Be(20_000);
        charge.AmountMinor.Should().Be(72_000);
    }

    [Fact]
    public void An_invoice_can_always_be_reconciled_from_its_parts()
    {
        // The property that matters more than any single figure: gross, less both reductions, is
        // what was taxed; plus tax, less credit, is what was charged.
        var subscription = SubscribedWithBand(
            unitAmountMinor: 3_333,
            quantity: 7,
            bandBasisPoints: 500,
            automaticBasisPoints: 800,
            combination: AutomaticDiscountCombination.Additive);
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Price.TaxMode = TaxMode.Exclusive;
        subscription.CreditBalanceMinor = 1_000;
        subscription.Discount = new DiscountTerms
        {
            Code = "stackable",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000
        };
        subscription.Plan.QuantityDiscountCombinationPolicy =
            QuantityDiscountCombinationPolicy.Stack;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        var discounted = charge.GrossAmountMinor
            - charge.BuiltInDiscountMinor
            - charge.PromotionalDiscountMinor;

        discounted.Should().Be(charge.NetAmountMinor);
        charge.AmountMinor.Should().Be(
            charge.NetAmountMinor + charge.TaxAmountMinor - charge.CreditConsumedMinor);
    }

    [Fact]
    public void A_renewal_prices_from_the_snapshot_rather_than_the_catalogue()
    {
        // The subscriber holds their own copy of the discount, so what the catalogue says now is
        // irrelevant to what they are charged. That the copy is *taken* is asserted where the
        // subscription is built — see SubscriptionCreationServiceTests; this is the half that
        // matters to the money.
        var sold = new PriceSnapshot
        {
            PriceId = "price-yearly",
            UnitAmountMinor = 100_000,
            AutomaticDiscountBasisPoints = 800,
            QuantityDiscountCombination = AutomaticDiscountCombination.Additive,
            PriceVersion = 3
        };

        var subscription = new SubscriptionDetail { Price = sold, Plan = new PlanSnapshot() };

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now)
            .AmountMinor.Should().Be(92_000);
    }

    [Fact]
    public void A_plan_change_prices_the_target_price_with_the_targets_own_discount()
    {
        // Moving from the monthly price to the yearly one, halfway through the month. What matters
        // here is only that the new side is discounted and the old side is not.
        var subscription = new SubscriptionDetail
        {
            Plan = new PlanSnapshot(),
            Price = new PriceSnapshot { UnitAmountMinor = 10_000 },
            CurrentPeriodStartUtc = Now.AddDays(-15),
            CurrentPeriodEndUtc = Now.AddDays(15)
        };

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            subscription.Plan,
            new PriceSnapshot
            {
                UnitAmountMinor = 100_000,
                AutomaticDiscountBasisPoints = 800,
                QuantityDiscountCombination = AutomaticDiscountCombination.BestDiscount
            },
            [],
            Now,
            Now.AddDays(-15),
            Now.AddDays(350));

        var undiscounted = SubscriptionProrationCalculator.Calculate(
            subscription,
            subscription.Plan,
            new PriceSnapshot { UnitAmountMinor = 100_000 },
            [],
            Now,
            Now.AddDays(-15),
            Now.AddDays(350));

        outcome.ChargeMinor.Should().BeLessThan(undiscounted.ChargeMinor,
            "the target price's own 8% has to reach the proration, not just the renewal");
        outcome.ChargeMinor.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_request_accepts_the_combination_by_name()
    {
        var request = JsonSerializer.Deserialize<CreatePriceRequest>(
            """{"automaticDiscountBasisPoints":800,"quantityDiscountCombination":"Additive"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        request!.AutomaticDiscountBasisPoints.Should().Be(800);
        request.QuantityDiscountCombination.Should().Be(AutomaticDiscountCombination.Additive);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(0, null, null)]
    [InlineData(800, null, "BestDiscount")]
    [InlineData(800, AutomaticDiscountCombination.Additive, "Additive")]
    public void A_combination_is_only_reported_where_there_is_a_discount_to_combine(
        int? basisPoints,
        AutomaticDiscountCombination? stored,
        string? expected)
    {
        SubscriptionDiscountPresentation.Describe(basisPoints, stored).Should().Be(expected);
    }

    private static SubscriptionDetail Subscribed(
        long unitAmountMinor,
        int? automaticBasisPoints,
        AutomaticDiscountCombination? combination = null) => new()
    {
        Plan = new PlanSnapshot(),
        Price = new PriceSnapshot
        {
            PriceId = "price-1",
            UnitAmountMinor = unitAmountMinor,
            AutomaticDiscountBasisPoints = automaticBasisPoints,
            QuantityDiscountCombination = combination
        }
    };

    private static SubscriptionDetail SubscribedWithBand(
        long unitAmountMinor,
        long quantity,
        int bandBasisPoints,
        int? automaticBasisPoints,
        AutomaticDiscountCombination? combination) => new()
    {
        Plan = new PlanSnapshot
        {
            QuantityItems =
            [
                new PlanQuantityItem
                {
                    ItemKey = "seats",
                    QuantityDiscountTiers =
                    [
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 1,
                            DiscountBasisPoints = bandBasisPoints
                        }
                    ]
                }
            ]
        },
        Price = new PriceSnapshot
        {
            PriceId = "price-1",
            UnitAmountMinor = unitAmountMinor,
            QuantityItemKey = "seats",
            AutomaticDiscountBasisPoints = automaticBasisPoints,
            QuantityDiscountCombination = combination
        },
        QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seats",
                Quantity = quantity,
                UnitAmountMinor = unitAmountMinor
            }
        ]
    };
}
