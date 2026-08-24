using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// What a price's tax mode does to the money.
/// </summary>
/// <remarks>
/// The two modes are two readings of the same configured number, and the difference between them is
/// the tax itself: CHF 145.00 at 7.7% is either CHF 156.17 or CHF 145.00 to the customer. Every test
/// here is about which of those two a given configuration means.
/// <para>
/// The rate used throughout is Switzerland's 7.7%, because it divides badly on purpose — a rate that
/// went in evenly would hide every rounding question these tests exist to pin down.
/// </para>
/// </remarks>
public sealed class SubscriptionTaxModeTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_exclusive_price_is_charged_above_the_configured_amount()
    {
        var subscription = Subscribed(14_500, 770, TaxMode.Exclusive);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // 7.7% of 14,500 is 1,116.5, rounded to 1,117. The customer pays CHF 156.17.
        charge.NetAmountMinor.Should().Be(14_500);
        charge.TaxAmountMinor.Should().Be(1_117);
        charge.AmountMinor.Should().Be(15_617);
    }

    [Fact]
    public void An_inclusive_price_is_charged_exactly_the_configured_amount()
    {
        var subscription = Subscribed(14_500, 770, TaxMode.Inclusive);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // The whole point: the customer pays the number on the pricing page, and the tax is found
        // inside it — 14,500 × 770 / 10,770 = 1,036.6, rounded to 1,037.
        charge.AmountMinor.Should().Be(14_500);
        charge.TaxAmountMinor.Should().Be(1_037);
        charge.NetAmountMinor.Should().Be(13_463);
    }

    [Fact]
    public void An_inclusive_charge_always_splits_back_into_itself()
    {
        // The property that matters more than any single figure: net plus tax is the total, by
        // construction rather than by a second calculation. An invoice whose lines do not add up to
        // what was charged is the failure this rules out.
        foreach (var amount in new long[] { 1, 99, 100, 14_500, 999_999, 1_000_000_007 })
        {
            var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
                Subscribed(amount, 770, TaxMode.Inclusive), Now);

            charge.NetAmountMinor.Should().Be(
                charge.AmountMinor - charge.TaxAmountMinor,
                $"the split of {amount} has to close");
        }
    }

    [Fact]
    public void A_price_with_a_rate_but_no_mode_is_charged_the_way_it_always_was()
    {
        // Every price authored before modes existed is this shape, and every subscription sold on
        // one was charged tax on top. Reading it as inclusive would quietly cut the merchant's
        // revenue on live subscriptions by the tax.
        var subscription = Subscribed(14_500, 770, taxMode: null);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(15_617);
        charge.NetAmountMinor.Should().Be(14_500);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_price_with_no_rate_is_untaxed_whatever_its_mode_says(int? rate)
    {
        // A mode without a rate is meaningless rather than invalid — a builder that clears the
        // percentage but leaves the selector alone must not start charging a zero-rate tax, or worse
        // extract one from the amount.
        var subscription = Subscribed(14_500, rate, TaxMode.Inclusive);

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.AmountMinor.Should().Be(14_500);
        charge.TaxAmountMinor.Should().Be(0);
        charge.NetAmountMinor.Should().Be(14_500);
    }

    [Fact]
    public void A_hundred_percent_inclusive_rate_is_half_tax()
    {
        // The boundary the validator allows, and the one place the inclusive formula visibly differs
        // from the exclusive one: 100% *of the net* means half the total is tax.
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(1_000, 10_000, TaxMode.Inclusive), Now);

        charge.AmountMinor.Should().Be(1_000);
        charge.TaxAmountMinor.Should().Be(500);
        charge.NetAmountMinor.Should().Be(500);
    }

    [Fact]
    public void A_fractional_rate_is_carried_at_basis_point_precision()
    {
        // 7.75%, which is why the rate is stored in basis points rather than whole percent.
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(20_000, 775, TaxMode.Exclusive), Now);

        charge.TaxAmountMinor.Should().Be(1_550);
        charge.AmountMinor.Should().Be(21_550);
    }

    [Fact]
    public void A_discount_reduces_the_taxable_amount_in_both_modes()
    {
        var discount = new DiscountTerms
        {
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_000
        };

        var exclusive = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(10_000, 1_000, TaxMode.Exclusive, discount), Now);
        var inclusive = SubscriptionAmountCalculator.PeriodAmountMinor(
            Subscribed(10_000, 1_000, TaxMode.Inclusive, discount), Now);

        // Discounted to 8,000 first, then split. Taxing before discounting would charge tax on
        // money nobody paid.
        exclusive.NetAmountMinor.Should().Be(8_000);
        exclusive.TaxAmountMinor.Should().Be(800);
        exclusive.AmountMinor.Should().Be(8_800);

        inclusive.AmountMinor.Should().Be(8_000);
        inclusive.TaxAmountMinor.Should().Be(727, "8,000 × 1,000 / 11,000");
        inclusive.NetAmountMinor.Should().Be(7_273);
    }

    [Fact]
    public void Credit_is_spent_after_tax_and_does_not_shrink_the_taxable_base()
    {
        var subscription = Subscribed(10_000, 1_000, TaxMode.Inclusive);
        subscription.CreditBalanceMinor = 3_000;

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        // The bill is still 10,000 with 909 of tax inside it; the credit pays part of that bill
        // rather than changing what the bill was for.
        charge.NetAmountMinor.Should().Be(9_091);
        charge.TaxAmountMinor.Should().Be(909);
        charge.CreditConsumedMinor.Should().Be(3_000);
        charge.AmountMinor.Should().Be(7_000);
    }

    [Fact]
    public void Tax_is_calculated_once_on_the_aggregate_rather_than_per_unit()
    {
        // Three units at 3.33 with 7.7% tax. Per unit the tax rounds to 26 each, 78 in total; on
        // the aggregate it is 77. The aggregate is the charge, so the aggregate is what is taxed.
        var subscription = Subscribed(0, 770, TaxMode.Exclusive);
        subscription.Price.UnitAmountMinor = 333;
        subscription.Price.QuantityItemKey = "seats";
        subscription.QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "seats",
                Quantity = 3,
                UnitAmountMinor = 333
            }
        ];

        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(subscription, Now);

        charge.NetAmountMinor.Should().Be(999);
        charge.TaxAmountMinor.Should().Be(77);
        charge.AmountMinor.Should().Be(1_076);
    }

    [Fact]
    public void The_first_charge_uses_the_same_split_as_a_renewal()
    {
        // Two entry points priced by one calculation. A signup and its first renewal charging
        // different amounts for the same period would be the worst kind of tax bug: invisible until
        // a month later.
        var exclusive = Subscribed(14_500, 770, TaxMode.Exclusive);
        var inclusive = Subscribed(14_500, 770, TaxMode.Inclusive);

        SubscriptionAmountCalculator.PeriodAmountMinor(exclusive).Should().Be(
            SubscriptionAmountCalculator.PeriodAmountMinor(exclusive, Now).AmountMinor);
        SubscriptionAmountCalculator.PeriodAmountMinor(inclusive).Should().Be(
            SubscriptionAmountCalculator.PeriodAmountMinor(inclusive, Now).AmountMinor);

        SubscriptionAmountCalculator.PeriodAmountMinor(inclusive).Should().Be(14_500);
    }

    private static SubscriptionDetail Subscribed(
        long unitAmountMinor,
        int? taxRateBasisPoints,
        TaxMode? taxMode,
        DiscountTerms? discount = null) => new()
    {
        Price = new PriceSnapshot
        {
            UnitAmountMinor = unitAmountMinor,
            TaxRateBasisPoints = taxRateBasisPoints,
            TaxMode = taxMode
        },
        Discount = discount
    };
}
