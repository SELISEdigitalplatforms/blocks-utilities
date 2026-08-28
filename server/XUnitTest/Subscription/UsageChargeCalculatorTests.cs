using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Discounting and taxing a metered overage amount — shared by period-end rating and the overage
/// preview, so both must agree exactly.
/// </summary>
public sealed class UsageChargeCalculatorTests
{
    [Fact]
    public void No_discount_and_no_tax_charges_the_gross_untouched()
    {
        var charge = UsageChargeCalculator.Charge(2_000, NewPrice());

        charge.GrossMinor.Should().Be(2_000);
        charge.AutomaticDiscountMinor.Should().Be(0);
        charge.NetMinor.Should().Be(2_000);
        charge.TaxMinor.Should().Be(0);
        charge.TotalMinor.Should().Be(2_000);
    }

    [Fact]
    public void An_automatic_discount_reduces_the_amount_before_tax()
    {
        var price = NewPrice(automaticDiscountBasisPoints: 800, taxRateBasisPoints: 1_000);

        var charge = UsageChargeCalculator.Charge(2_000, price);

        charge.AutomaticDiscountMinor.Should().Be(160);
        charge.NetMinor.Should().Be(1_840);
        charge.TaxMinor.Should().Be(184);
        charge.TotalMinor.Should().Be(2_024);
    }

    [Fact]
    public void Exclusive_tax_is_added_on_top_of_the_net_amount()
    {
        var price = NewPrice(taxRateBasisPoints: 770, taxMode: TaxMode.Exclusive);

        var charge = UsageChargeCalculator.Charge(2_000, price);

        charge.NetMinor.Should().Be(2_000);
        charge.TaxMinor.Should().Be(154);
        charge.TotalMinor.Should().Be(2_154);
    }

    [Fact]
    public void Inclusive_tax_is_extracted_from_the_configured_amount()
    {
        var price = NewPrice(taxRateBasisPoints: 1_000, taxMode: TaxMode.Inclusive);

        var charge = UsageChargeCalculator.Charge(2_000, price);

        charge.TotalMinor.Should().Be(2_000);
        charge.TaxMinor.Should().Be(182);
        charge.NetMinor.Should().Be(1_818);
    }

    [Theory]
    [InlineData(AutomaticDiscountCombination.BestDiscount)]
    [InlineData(AutomaticDiscountCombination.Additive)]
    public void Either_combination_policy_is_honoured_when_only_an_automatic_discount_applies(
        AutomaticDiscountCombination combination)
    {
        // No volume band ever reaches metered usage, so with only an automatic discount present
        // both combination policies must agree — there is nothing to combine with.
        var price = NewPrice(automaticDiscountBasisPoints: 500, quantityDiscountCombination: combination);

        var charge = UsageChargeCalculator.Charge(1_000, price);

        charge.AutomaticDiscountMinor.Should().Be(50);
        charge.TotalMinor.Should().Be(950);
    }

    [Fact]
    public void The_difference_of_two_charges_matches_a_field_by_field_subtraction()
    {
        var price = NewPrice(automaticDiscountBasisPoints: 800, taxRateBasisPoints: 770);

        var current = UsageChargeCalculator.Charge(2_000, price);
        var projected = UsageChargeCalculator.Charge(12_000, price);

        var additional = UsageChargeCalculator.Difference(projected, current);

        additional.GrossMinor.Should().Be(projected.GrossMinor - current.GrossMinor);
        additional.AutomaticDiscountMinor.Should().Be(
            projected.AutomaticDiscountMinor - current.AutomaticDiscountMinor);
        additional.NetMinor.Should().Be(projected.NetMinor - current.NetMinor);
        additional.TaxMinor.Should().Be(projected.TaxMinor - current.TaxMinor);
        additional.TotalMinor.Should().Be(projected.TotalMinor - current.TotalMinor);
    }

    [Fact]
    public void The_matched_period_end_example_produces_the_documented_figures()
    {
        // The exact figures from the overage preview's documented example: 20 current overage
        // units and 120 projected overage units, both at 100 minor units each, no discount, 7.7%
        // exclusive tax.
        var price = NewPrice(taxRateBasisPoints: 770, taxMode: TaxMode.Exclusive);

        var current = UsageChargeCalculator.Charge(2_000, price);
        var projected = UsageChargeCalculator.Charge(12_000, price);
        var additional = UsageChargeCalculator.Difference(projected, current);

        current.TaxMinor.Should().Be(154);
        current.TotalMinor.Should().Be(2_154);
        projected.TaxMinor.Should().Be(924);
        projected.TotalMinor.Should().Be(12_924);
        additional.GrossMinor.Should().Be(10_000);
        additional.TaxMinor.Should().Be(770);
        additional.TotalMinor.Should().Be(10_770);
    }

    private static PriceSnapshot NewPrice(
        int? automaticDiscountBasisPoints = null,
        int? taxRateBasisPoints = null,
        TaxMode? taxMode = null,
        AutomaticDiscountCombination? quantityDiscountCombination = null) => new()
    {
        CurrencyCode = "CHF",
        AutomaticDiscountBasisPoints = automaticDiscountBasisPoints,
        TaxRateBasisPoints = taxRateBasisPoints,
        TaxMode = taxMode,
        QuantityDiscountCombination = quantityDiscountCombination
    };
}
