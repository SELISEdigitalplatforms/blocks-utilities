using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Which volume band a seat count selects, and how it combines with a promotional code.
/// </summary>
/// <remarks>
/// The bands under test are the commercial model this was built for: CHF 145 per user per month,
/// discounted 0/5/10/15/20% at 1/5/10/20/30 users.
/// </remarks>
public sealed class QuantityDiscountCalculatorTests
{
    private const long UnitAmount = 14_500;

    [Theory]
    // The boundary either side of every band edge, which is where an off-by-one hides.
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 500)]
    [InlineData(9, 500)]
    [InlineData(10, 1_000)]
    [InlineData(19, 1_000)]
    [InlineData(20, 1_500)]
    [InlineData(29, 1_500)]
    [InlineData(30, 2_000)]
    [InlineData(400, 2_000)]
    public void A_quantity_selects_the_band_it_falls_in(long quantity, int expectedBasisPoints)
    {
        QuantityDiscountCalculator.Resolve(Bands(), UnitAmount, quantity)
            .DiscountBasisPoints.Should().Be(expectedBasisPoints);
    }

    [Fact]
    public void The_band_reduces_the_whole_quantity_charge()
    {
        // CHF 145 x 10 = CHF 1,450 gross; tier 3 is 10%, so CHF 145 off and CHF 1,305 to pay.
        var outcome = QuantityDiscountCalculator.Resolve(Bands(), UnitAmount, 10);

        outcome.GrossAmountMinor.Should().Be(145_000);
        outcome.DiscountAmountMinor.Should().Be(14_500);
        outcome.SubtotalMinor.Should().Be(130_500);
    }

    [Fact]
    public void A_plan_authored_without_bands_prices_exactly_as_it_always_did()
    {
        var outcome = QuantityDiscountCalculator.Resolve(tiers: null, UnitAmount, 10);

        outcome.Tier.Should().BeNull();
        outcome.DiscountAmountMinor.Should().Be(0);
        outcome.SubtotalMinor.Should().Be(145_000);
    }

    [Fact]
    public void A_flat_fee_price_has_no_quantity_to_band()
    {
        var outcome = QuantityDiscountCalculator.ResolveFrom(
            Plan(),
            new PriceSnapshot { UnitAmountMinor = 8_900, QuantityItemKey = null },
            []);

        outcome.DiscountAmountMinor.Should().Be(0);
        outcome.SubtotalMinor.Should().Be(8_900);
    }

    [Fact]
    public void The_band_comes_from_the_subscribers_snapshot_not_todays_catalogue()
    {
        // The subscriber holds a snapshot whose only band is 20% from one user. Repricing the
        // catalogue must not reach them, which is the whole point of snapshotting the bands.
        var plan = Plan();
        plan.QuantityItems[0].QuantityDiscountTiers =
        [
            new QuantityDiscountTier
            {
                MinimumQuantity = 1,
                MaximumQuantity = null,
                DiscountBasisPoints = 2_000
            }
        ];

        QuantityDiscountCalculator.ResolveFrom(plan, Price(), Held(2))
            .DiscountBasisPoints.Should().Be(2_000);
    }

    [Fact]
    public void The_unit_amount_snapshotted_on_the_item_beats_the_prices_own()
    {
        // Adding units later charges what the subscriber agreed to, not today's list price.
        var held = Held(5);
        held[0].UnitAmountMinor = 10_000;

        QuantityDiscountCalculator.ResolveFrom(plan: Plan(), price: Price(), quantityItems: held)
            .GrossAmountMinor.Should().Be(50_000);
    }

    [Theory]
    // 10 users: gross 145,000. Band is 10% = 14,500. The promotion is a flat 5% = 7,250.
    [InlineData(QuantityDiscountCombinationPolicy.BestDiscount, 130_500)]
    [InlineData(QuantityDiscountCombinationPolicy.QuantityOnly, 130_500)]
    [InlineData(QuantityDiscountCombinationPolicy.Stack, 123_975)]
    public void The_plans_policy_decides_how_a_band_and_a_promotion_combine(
        QuantityDiscountCombinationPolicy policy,
        long expected)
    {
        var subscription = Subscription(policy);

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription, DateTime.UtcNow)
            .AmountMinor.Should().Be(expected);
    }

    [Fact]
    public void A_promotion_that_loses_to_the_band_is_not_counted_as_spent()
    {
        // Otherwise three months of "5% off" expire on periods where the band was larger and the
        // customer never sees the promotion they were given.
        var subscription = Subscription(QuantityDiscountCombinationPolicy.BestDiscount);

        SubscriptionAmountCalculator.PeriodAmountMinor(subscription, DateTime.UtcNow)
            .DiscountApplied.Should().BeFalse();
    }

    [Fact]
    public void A_promotion_that_beats_the_band_is_counted_as_spent()
    {
        var subscription = Subscription(QuantityDiscountCombinationPolicy.BestDiscount);
        subscription.Discount!.PercentBasisPoints = 5_000;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, DateTime.UtcNow);

        charge.AmountMinor.Should().Be(72_500);
        charge.DiscountApplied.Should().BeTrue();
    }

    private static List<QuantityDiscountTier> Bands() =>
    [
        new() { MinimumQuantity = 1, MaximumQuantity = 4, DiscountBasisPoints = 0 },
        new() { MinimumQuantity = 5, MaximumQuantity = 9, DiscountBasisPoints = 500 },
        new() { MinimumQuantity = 10, MaximumQuantity = 19, DiscountBasisPoints = 1_000 },
        new() { MinimumQuantity = 20, MaximumQuantity = 29, DiscountBasisPoints = 1_500 },
        new() { MinimumQuantity = 30, MaximumQuantity = null, DiscountBasisPoints = 2_000 }
    ];

    private static PlanSnapshot Plan() => new()
    {
        Code = "team",
        QuantityItems =
        [
            new PlanQuantityItem
            {
                ItemKey = "user",
                UnitLabel = "user",
                MinQuantity = 1,
                QuantityDiscountTiers = Bands()
            }
        ]
    };

    private static PriceSnapshot Price() => new()
    {
        UnitAmountMinor = UnitAmount,
        CurrencyCode = "CHF",
        QuantityItemKey = "user"
    };

    private static List<SubscriptionQuantityItem> Held(long quantity) =>
    [
        new()
        {
            ItemKey = "user",
            UnitLabel = "user",
            Quantity = quantity,
            UnitAmountMinor = UnitAmount
        }
    ];

    private static SubscriptionDetail Subscription(QuantityDiscountCombinationPolicy policy)
    {
        var plan = Plan();
        plan.QuantityDiscountCombinationPolicy = policy;

        return new SubscriptionDetail
        {
            Plan = plan,
            Price = Price(),
            QuantityItems = Held(10),
            CurrencyCode = "CHF",
            Discount = new DiscountTerms
            {
                Code = "welcome",
                Kind = DiscountKind.Percent,
                PercentBasisPoints = 500
            }
        };
    }
}
