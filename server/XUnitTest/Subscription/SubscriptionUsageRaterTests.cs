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

    private static MeterTier Tier(long? upTo, long unitAmountMinor) =>
        new() { UpToQuantity = upTo, UnitAmountMinor = unitAmountMinor };

    private static PlanMeter NewMeter(long includedQuantity, List<MeterTier> tiers) => new()
    {
        MeterKey = "screening",
        IncludedQuantity = includedQuantity,
        RateTables = [new MeterRateTable { CurrencyCode = "CHF", Tiers = tiers }]
    };
}
