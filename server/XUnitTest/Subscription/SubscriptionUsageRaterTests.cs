using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>What a meter's usage costs beyond its included quantity.</summary>
public sealed class SubscriptionUsageRaterTests
{
    [Fact]
    public void Usage_under_the_included_quantity_rates_to_zero()
    {
        var meter = NewMeter(includedQuantity: 500, tiers: [Tier(null, 10)]);

        SubscriptionUsageRater.OverageAmountMinor(meter, 300, "CHF").Should().Be(0);
    }

    [Fact]
    public void Usage_exactly_at_the_included_quantity_rates_to_zero()
    {
        var meter = NewMeter(includedQuantity: 500, tiers: [Tier(null, 10)]);

        SubscriptionUsageRater.OverageAmountMinor(meter, 500, "CHF").Should().Be(0);
    }

    [Fact]
    public void Overage_entirely_within_the_first_tier_never_reaches_the_second_rate()
    {
        var meter = NewMeter(
            includedQuantity: 500,
            tiers: [Tier(400, 10), Tier(null, 5)]);

        // 100 overage units, all inside the first 400-unit band.
        SubscriptionUsageRater.OverageAmountMinor(meter, 600, "CHF").Should().Be(1_000);
    }

    [Fact]
    public void Overage_split_across_two_tiers_charges_each_slice_at_its_own_rate()
    {
        var meter = NewMeter(
            includedQuantity: 500,
            tiers: [Tier(400, 10), Tier(null, 5)]);

        // 700 overage units: 400 at 10 (=4,000) + 300 at 5 (=1,500) = 5,500.
        SubscriptionUsageRater.OverageAmountMinor(meter, 1_200, "CHF").Should().Be(5_500);
    }

    [Fact]
    public void The_final_unbounded_tier_absorbs_whatever_remains()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(10, 100), Tier(20, 50), Tier(null, 1)]);

        // 10 at 100 (=1,000) + 10 at 50 (=500) + 980 at 1 (=980) = 2,480.
        SubscriptionUsageRater.OverageAmountMinor(meter, 1_000, "CHF").Should().Be(2_480);
    }

    [Fact]
    public void Overage_allowed_false_always_rates_to_zero()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 10)]);
        meter.OverageAllowed = false;

        SubscriptionUsageRater.OverageAmountMinor(meter, 1_000, "CHF").Should().Be(0);
    }

    [Fact]
    public void No_matching_currency_rates_to_zero_rather_than_throwing()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 10)]);

        SubscriptionUsageRater.OverageAmountMinor(meter, 1_000, "EUR").Should().Be(0);
    }

    [Fact]
    public void Currency_matching_is_case_insensitive()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 10)]);
        meter.RateTables[0].CurrencyCode = "chf";

        SubscriptionUsageRater.OverageAmountMinor(meter, 100, "CHF").Should().Be(1_000);
    }

    [Fact]
    public void Allocations_for_the_full_range_report_every_tier_band_that_contributed()
    {
        var meter = NewMeter(includedQuantity: 500, tiers: [Tier(400, 10), Tier(null, 5)]);

        var result = SubscriptionUsageRater.OverageAllocations(meter, 700, "CHF");

        result.TotalAmountMinor.Should().Be(5_500);
        result.Allocations.Should().HaveCount(2);
        result.Allocations[0].FromOverageQuantity.Should().Be(1);
        result.Allocations[0].ToOverageQuantity.Should().Be(400);
        result.Allocations[0].Units.Should().Be(400);
        result.Allocations[0].UnitAmountMinor.Should().Be(10);
        result.Allocations[0].AmountMinor.Should().Be(4_000);
        result.Allocations[1].FromOverageQuantity.Should().Be(401);
        result.Allocations[1].ToOverageQuantity.Should().Be(700);
        result.Allocations[1].Units.Should().Be(300);
        result.Allocations[1].UnitAmountMinor.Should().Be(5);
        result.Allocations[1].AmountMinor.Should().Be(1_500);
    }

    [Fact]
    public void Allocations_starting_partway_through_a_tier_report_only_the_units_after_that_point()
    {
        var meter = NewMeter(includedQuantity: 150, tiers: [Tier(null, 100)]);

        // 20 overage already billed; 100 more requested — units 21 through 120.
        var result = SubscriptionUsageRater.OverageAllocations(
            meter, overageUnits: 120, currencyCode: "CHF", fromOverageUnitsExclusive: 20);

        result.TotalAmountMinor.Should().Be(10_000);
        result.Allocations.Should().ContainSingle();
        result.Allocations[0].FromOverageQuantity.Should().Be(21);
        result.Allocations[0].ToOverageQuantity.Should().Be(120);
        result.Allocations[0].Units.Should().Be(100);
        result.Allocations[0].AmountMinor.Should().Be(10_000);
    }

    [Fact]
    public void Allocations_crossing_a_tier_boundary_split_the_additional_units_correctly()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(400, 10), Tier(null, 5)]);

        // Already 350 overage units billed (all inside the first tier); 100 more requested spans
        // the remaining 50 units of the first tier plus 50 of the second.
        var result = SubscriptionUsageRater.OverageAllocations(
            meter, overageUnits: 450, currencyCode: "CHF", fromOverageUnitsExclusive: 350);

        result.TotalAmountMinor.Should().Be(750); // 50 * 10 + 50 * 5
        result.Allocations.Should().HaveCount(2);
        result.Allocations[0].FromOverageQuantity.Should().Be(351);
        result.Allocations[0].ToOverageQuantity.Should().Be(400);
        result.Allocations[0].Units.Should().Be(50);
        result.Allocations[0].AmountMinor.Should().Be(500);
        result.Allocations[1].FromOverageQuantity.Should().Be(401);
        result.Allocations[1].ToOverageQuantity.Should().Be(450);
        result.Allocations[1].Units.Should().Be(50);
        result.Allocations[1].AmountMinor.Should().Be(250);
    }

    [Fact]
    public void Allocations_with_nothing_additional_beyond_the_starting_point_are_empty()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 10)]);

        var result = SubscriptionUsageRater.OverageAllocations(
            meter, overageUnits: 100, currencyCode: "CHF", fromOverageUnitsExclusive: 100);

        result.TotalAmountMinor.Should().Be(0);
        result.Allocations.Should().BeEmpty();
    }

    [Fact]
    public void The_total_returning_method_matches_the_allocation_totals_sum()
    {
        var meter = NewMeter(includedQuantity: 500, tiers: [Tier(400, 10), Tier(null, 5)]);

        var total = SubscriptionUsageRater.OverageAmountMinor(meter, 1_200, "CHF");
        var allocations = SubscriptionUsageRater.OverageAllocations(meter, 700, "CHF");

        total.Should().Be(allocations.TotalAmountMinor);
    }

    /// <summary>
    /// A technically valid unit rate and a technically valid (if unusual) overage quantity can
    /// still multiply past <c>long.MaxValue</c> once the <c>Int128</c> tier total is narrowed back
    /// down to <c>long</c> — checked arithmetic throughout WalkTierRange must throw rather than
    /// silently wrap into a mispriced (and possibly negative) charge.
    /// </summary>
    [Fact]
    public void A_tier_total_that_would_overflow_a_long_throws_rather_than_wraps()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(null, 5_000_000_000_000_000_000)]);

        // 3 units at 5 quintillion each is ~15 quintillion, past long.MaxValue (~9.2 quintillion).
        var act = () => SubscriptionUsageRater.OverageAllocations(meter, overageUnits: 3, currencyCode: "CHF");

        act.Should().Throw<OverflowException>();
    }

    /// <summary>
    /// A rate and quantity that individually fit comfortably in a <c>long</c>, and whose product
    /// also fits, must not be refused just because the intermediate arithmetic happens to use a
    /// wider type. Checked arithmetic must never reject a charge that was never actually going to
    /// overflow.
    /// </summary>
    [Fact]
    public void A_large_but_valid_tier_total_still_prices_correctly()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(null, 1_000_000_000_000)]);

        var result = SubscriptionUsageRater.OverageAllocations(
            meter, overageUnits: 1_000_000, currencyCode: "CHF");

        result.TotalAmountMinor.Should().Be(1_000_000_000_000L * 1_000_000);
    }

    // ------------------------------------------------------------------ fractional quantities

    /// <summary>
    /// Half a unit at a whole-unit rate costs half that rate, and the meter's total is what rounds.
    /// </summary>
    [Fact]
    public void A_fractional_overage_is_priced_exactly_and_rounded_once()
    {
        var meter = NewMeter(includedQuantity: 500, tiers: [Tier(null, 3)], scale: 1);

        // 0.5 overage units at 3 minor each = 1.5 minor, which rounds up to 2.
        SubscriptionUsageRater.OverageAmountMinor(meter, 500.5m, "CHF").Should().Be(2);
    }

    /// <summary>
    /// Re-banding a rate table without changing any of its prices cannot change the bill.
    /// </summary>
    /// <remarks>
    /// The property that makes "round once per meter" the right policy. Rounding each band and
    /// summing would let the arrangement of the table decide the total: three bands at the same
    /// rate would round three times and one band would round once.
    /// </remarks>
    [Fact]
    public void Splitting_a_band_at_the_same_rate_does_not_change_the_total()
    {
        var oneBand = NewMeter(includedQuantity: 0, tiers: [Tier(null, 1)], scale: 6);
        var threeBands = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(0.3m, 1), Tier(0.6m, 1), Tier(null, 1)],
            scale: 6);

        var overage = 0.9m;

        SubscriptionUsageRater.OverageAmountMinor(threeBands, overage, "CHF")
            .Should()
            .Be(SubscriptionUsageRater.OverageAmountMinor(oneBand, overage, "CHF"));
    }

    /// <summary>
    /// The band breakdown carries exact amounts, so it sums to the figure that was rounded.
    /// </summary>
    /// <remarks>
    /// Reported exactly rather than rounded per band precisely because only the total is whole. A
    /// breakdown whose rows had each been rounded would not add up to the charge beside it.
    /// </remarks>
    [Fact]
    public void The_band_breakdown_sums_to_the_amount_that_was_rounded()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(0.5m, 3), Tier(null, 1)],
            scale: 1);

        var result = SubscriptionUsageRater.OverageAllocations(meter, 0.8m, "CHF");

        // 0.5 at 3 (=1.5) + 0.3 at 1 (=0.3) = 1.8 exactly, which rounds to 2.
        result.Allocations.Sum(allocation => allocation.AmountMinor).Should().Be(1.8m);
        result.TotalAmountMinor.Should().Be(2);
    }

    /// <summary>
    /// A band's first quantity is the smallest one its meter can distinguish above the band's
    /// open lower bound.
    /// </summary>
    /// <remarks>
    /// At scale zero that is the whole unit the band has always started at — the case pinned by
    /// the whole-unit tests above, which still report 1 and 401. A three-place meter reports
    /// 400.001, because 401 would leave the quantities between undescribed.
    /// </remarks>
    [Fact]
    public void A_bands_first_quantity_is_one_step_above_its_open_lower_bound()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(400, 10), Tier(null, 5)],
            scale: 3);

        var result = SubscriptionUsageRater.OverageAllocations(meter, 500m, "CHF");

        result.Allocations[0].FromOverageQuantity.Should().Be(0.001m);
        result.Allocations[0].ToOverageQuantity.Should().Be(400m);
        result.Allocations[1].FromOverageQuantity.Should().Be(400.001m);
        result.Allocations[1].ToOverageQuantity.Should().Be(500m);
    }

    /// <summary>
    /// A fraction sitting exactly on a band edge belongs to the band that closes there, not to the
    /// next one. Bands are closed above and open below, so no quantity falls into two.
    /// </summary>
    [Fact]
    public void A_quantity_on_a_band_edge_belongs_to_the_band_that_closes_there()
    {
        var meter = NewMeter(
            includedQuantity: 0,
            tiers: [Tier(0.5m, 100), Tier(null, 1)],
            scale: 1);

        var result = SubscriptionUsageRater.OverageAllocations(meter, 0.5m, "CHF");

        result.Allocations.Should().HaveCount(1);
        result.Allocations[0].ToOverageQuantity.Should().Be(0.5m);
        // 0.5 at 100 = 50 exactly. The second band is never entered.
        result.TotalAmountMinor.Should().Be(50);
    }

    /// <summary>
    /// Pricing only the addition, from a fractional already-billed prefix.
    /// </summary>
    /// <remarks>
    /// The preview's case. The prefix is exclusive, so the units already charged for are not
    /// charged again, and the reported band still counts from the period's first overage unit.
    /// </remarks>
    [Fact]
    public void A_fractional_prefix_is_excluded_from_the_priced_range()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 10)], scale: 2);

        var result = SubscriptionUsageRater.OverageAllocations(
            meter,
            overageUnits: 2.75m,
            currencyCode: "CHF",
            fromOverageUnitsExclusive: 1.25m);

        result.Allocations.Should().HaveCount(1);
        result.Allocations[0].Units.Should().Be(1.5m);
        result.Allocations[0].FromOverageQuantity.Should().Be(1.26m);
        result.TotalAmountMinor.Should().Be(15);
    }

    /// <summary>
    /// A third of a unit is held and priced exactly, with no binary residue.
    /// </summary>
    /// <remarks>
    /// The reason quantities are decimal rather than double. Three thirds priced at three minor
    /// units must come to exactly three, not to 2.9999999999999996.
    /// </remarks>
    [Fact]
    public void Thirds_of_a_unit_sum_without_residue()
    {
        var meter = NewMeter(includedQuantity: 0, tiers: [Tier(null, 3)], scale: 6);

        var result = SubscriptionUsageRater.OverageAllocations(meter, 0.333333m * 3, "CHF");

        result.Allocations[0].AmountMinor.Should().Be(2.999997m);
        result.TotalAmountMinor.Should().Be(3);
    }

    private static MeterTier Tier(decimal? upTo, long unitAmountMinor) =>
        new() { UpToQuantity = upTo, UnitAmountMinor = unitAmountMinor };

    private static PlanMeter NewMeter(
        decimal includedQuantity,
        List<MeterTier> tiers,
        int scale = 0) => new()
    {
        MeterKey = "screening",
        QuantityScale = scale,
        IncludedQuantity = includedQuantity,
        RateTables = [new MeterRateTable { CurrencyCode = "CHF", Tiers = tiers }]
    };
}
