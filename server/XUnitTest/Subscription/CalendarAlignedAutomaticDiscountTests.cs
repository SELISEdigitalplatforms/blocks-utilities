using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Calendar stubs meeting the reductions a subscriber gets without asking for them.
/// </summary>
/// <remarks>
/// The ordering is the whole subject: full gross, then the calendar-day fraction, then the
/// built-in reductions, then a promotional code, then tax, then credit. Proration is a property of
/// the <em>period</em>, so it happens before anything that reduces the price — a discount applied
/// to a whole month and then subtracted from a fraction of one is not a smaller discount, it is a
/// larger one.
/// <para>
/// Every figure below is against a seven-of-thirty-one-dates stub, so the same arithmetic a
/// 25 August signup would be charged.
/// </para>
/// </remarks>
public sealed class CalendarAlignedAutomaticDiscountTests
{
    private static readonly BillingDayFraction SevenOfThirtyOne = new(7, 31);
    private static readonly DateTime Now = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_automatic_discount_comes_off_the_prorated_gross()
    {
        // 7/31 of 8900 is 2010; 8% of that is 160.8, truncated to 160.
        Charge(FlatPrice(automaticBasisPoints: 800)).Should().Be(1_850);
    }

    /// <summary>
    /// The regression the ordering exists to prevent. 8% of a whole month is 712 — subtracting that
    /// from a 2010 stub would hand the subscriber a third of the stub as a discount.
    /// </summary>
    [Fact]
    public void An_automatic_discount_is_never_a_whole_months_worth_off_a_stub()
    {
        var stub = Charge(FlatPrice(automaticBasisPoints: 800));
        var wholeMonth = Charge(
            FlatPrice(automaticBasisPoints: 800),
            fraction: new BillingDayFraction(0, 0));

        stub.Should().Be(1_850);
        wholeMonth.Should().Be(8_188, "8% of 8900 is 712");
        (wholeMonth - stub).Should().BeGreaterThan(8_900 - 2_010 - 712,
            "the stub's discount must be a fraction of the month's, not the whole of it");
    }

    /// <summary>
    /// A volume band's resolved money is a whole month's, and is used verbatim when the band wins.
    /// It has to be re-expressed against the prorated gross or the same leak appears through the
    /// band instead of the automatic rate.
    /// </summary>
    [Fact]
    public void A_winning_volume_band_is_re_expressed_against_the_prorated_gross()
    {
        // 12 seats at 8900 is 106800 for a month; 7/31 of that is 24116. A 15% band on the stub is
        // 3617, not the 16020 that 15% of the whole month would be.
        Charge(SeatPrice(automaticBasisPoints: null), Plan(bandBasisPoints: 1_500), Seats(12))
            .Should().Be(20_499);
    }

    [Fact]
    public void Best_discount_takes_whichever_of_the_two_is_larger_on_the_stub()
    {
        // Automatic 8% of 24116 is 1929; the 5% band is 1205. The automatic rate wins.
        Charge(
                SeatPrice(automaticBasisPoints: 800, AutomaticDiscountCombination.BestDiscount),
                Plan(bandBasisPoints: 500),
                Seats(12))
            .Should().Be(22_187);

        // Turn the band up past it and the band wins instead, still on the prorated gross.
        Charge(
                SeatPrice(automaticBasisPoints: 800, AutomaticDiscountCombination.BestDiscount),
                Plan(bandBasisPoints: 1_500),
                Seats(12))
            .Should().Be(20_499);
    }

    [Fact]
    public void Additive_combines_the_two_rates_and_applies_them_once_to_the_stub()
    {
        // 8% + 5% is one 13% rate: 13% of 24116 is 3135.08, truncated to 3135.
        Charge(
                SeatPrice(automaticBasisPoints: 800, AutomaticDiscountCombination.Additive),
                Plan(bandBasisPoints: 500),
                Seats(12))
            .Should().Be(20_981);
    }

    /// <summary>
    /// A code the subscriber typed is settled against the built-in reduction by the plan's policy,
    /// after both have been expressed against the stub.
    /// </summary>
    [Fact]
    public void A_promotional_code_is_compared_against_the_built_in_reduction_on_the_stub()
    {
        var plan = Plan(bandBasisPoints: 0);
        var price = FlatPrice(automaticBasisPoints: 800);

        // 20% of the 2010 stub is 402, which beats the automatic 160.
        var charge = ChargeDetail(price, plan, [], Percent(2_000));

        charge.AmountMinor.Should().Be(1_608);
        charge.DiscountApplied.Should().BeTrue("the code reduced the charge, so it is being used");
    }

    [Fact]
    public void A_promotional_code_that_loses_to_the_automatic_rate_is_not_consumed()
    {
        // 1% of the stub is 20, against the automatic 160.
        var charge = ChargeDetail(
            FlatPrice(automaticBasisPoints: 800), Plan(bandBasisPoints: 0), [], Percent(100));

        charge.AmountMinor.Should().Be(1_850);
        charge.DiscountApplied.Should().BeFalse(
            "spending a month of a limited code on a period it did not reduce would expire it " +
            "without the subscriber ever seeing it");
    }

    [Fact]
    public void Stacking_applies_the_code_to_what_the_automatic_rate_left()
    {
        var plan = Plan(bandBasisPoints: 0);
        plan.QuantityDiscountCombinationPolicy = QuantityDiscountCombinationPolicy.Stack;

        // The automatic 8% leaves 1850; 20% of that is 370.
        ChargeDetail(FlatPrice(automaticBasisPoints: 800), plan, [], Percent(2_000))
            .AmountMinor.Should().Be(1_480);
    }

    /// <summary>
    /// A fixed sum is the one discount that has to shrink with the period, because it is not
    /// already a proportion of something that shrank.
    /// </summary>
    [Fact]
    public void A_fixed_code_is_prorated_while_the_automatic_rate_is_not()
    {
        var plan = Plan(bandBasisPoints: 0);
        plan.QuantityDiscountCombinationPolicy = QuantityDiscountCombinationPolicy.Stack;

        var fixedOff = new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.FixedAmount,
            AmountMinor = 1_000
        };

        // The automatic 8% leaves 1850, and 7/31 of the 1000 fixed discount is 226.
        ChargeDetail(FlatPrice(automaticBasisPoints: 800), plan, [], fixedOff)
            .AmountMinor.Should().Be(1_624);
    }

    [Fact]
    public void Tax_is_charged_on_the_stub_after_every_discount()
    {
        var price = FlatPrice(automaticBasisPoints: 800);
        price.TaxRateBasisPoints = 770;
        price.TaxMode = TaxMode.Exclusive;

        // 7.7% of the discounted 1850 is 142.45, rounded to 142.
        Charge(price).Should().Be(1_992);
    }

    /// <summary>
    /// A plan change onto a calendar-aligned price that carries an automatic discount: the target
    /// stub is priced by calendar dates with the discount already in it, and netted against the
    /// unused time on the plan being left.
    /// </summary>
    [Fact]
    public void A_plan_change_onto_a_discounted_calendar_price_nets_the_discounted_stub()
    {
        var subscription = new SubscriptionDetail
        {
            ItemId = "sub-1",
            CurrencyCode = "CHF",
            Plan = Plan(bandBasisPoints: 0),
            Price = new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = 8_900,
                Interval = BillingInterval.Month,
                IntervalCount = 1
            },
            // 16 of 31 days unused on 25 August.
            CurrentPeriodStartUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
        };

        var target = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 40_000,
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            BillingAlignment = BillingAlignment.CalendarMonth,
            AutomaticDiscountBasisPoints = 800
        };

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            Plan(bandBasisPoints: 0),
            target,
            [],
            Now,
            Now,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            SevenOfThirtyOne);

        // The stub is 7/31 of 40000 = 9032, less the automatic 8% of that (722) = 8310. The
        // outgoing side gives back 8900 x 16/31 = 4593.
        outcome.ChargeMinor.Should().Be(3_717);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    private static long Charge(
        PriceSnapshot price,
        PlanSnapshot? plan = null,
        List<SubscriptionQuantityItem>? quantities = null,
        BillingDayFraction? fraction = null) =>
        ChargeDetail(price, plan, quantities, null, fraction).AmountMinor;

    private static PeriodCharge ChargeDetail(
        PriceSnapshot price,
        PlanSnapshot? plan = null,
        List<SubscriptionQuantityItem>? quantities = null,
        DiscountTerms? discount = null,
        BillingDayFraction? fraction = null)
    {
        var subscription = new SubscriptionDetail
        {
            ItemId = "sub-1",
            CurrencyCode = "CHF",
            Plan = plan ?? Plan(bandBasisPoints: 0),
            Price = price,
            QuantityItems = quantities ?? [],
            Discount = discount
        };

        return SubscriptionAmountCalculator.FirstPeriodCharge(
            subscription,
            fraction ?? SevenOfThirtyOne,
            Now);
    }

    private static DiscountTerms Percent(int basisPoints) => new()
    {
        Code = "welcome",
        Kind = DiscountKind.Percent,
        PercentBasisPoints = basisPoints
    };

    private static List<SubscriptionQuantityItem> Seats(long quantity) =>
    [
        new SubscriptionQuantityItem
        {
            ItemKey = "seat",
            UnitLabel = "seat",
            Quantity = quantity,
            UnitAmountMinor = 8_900
        }
    ];

    private static PriceSnapshot FlatPrice(
        int? automaticBasisPoints,
        AutomaticDiscountCombination? combination = null) => new()
    {
        CurrencyCode = "CHF",
        UnitAmountMinor = 8_900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        AutomaticDiscountBasisPoints = automaticBasisPoints,
        QuantityDiscountCombination = combination
    };

    private static PriceSnapshot SeatPrice(
        int? automaticBasisPoints,
        AutomaticDiscountCombination? combination = null)
    {
        var price = FlatPrice(automaticBasisPoints, combination);
        price.QuantityItemKey = "seat";

        return price;
    }

    private static PlanSnapshot Plan(int bandBasisPoints) => new()
    {
        Code = "professional",
        DisplayName = "Professional",
        QuantityItems =
        [
            new PlanQuantityItem
            {
                ItemKey = "seat",
                UnitLabel = "seat",
                MinQuantity = 1,
                DefaultQuantity = 1,
                QuantityDiscountTiers = bandBasisPoints <= 0
                    ? []
                    :
                    [
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 1,
                            MaximumQuantity = null,
                            DiscountBasisPoints = bandBasisPoints
                        }
                    ]
            }
        ]
    };
}
