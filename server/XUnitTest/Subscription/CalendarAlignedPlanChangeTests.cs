using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Moving onto a calendar-aligned price part-way through a period.
/// </summary>
/// <remarks>
/// Two different prorations meet here, and they are deliberately not the same kind. What the
/// subscriber has left on the plan they are leaving is time they paid for, so it is credited by
/// elapsed time. What they are buying is a calendar stub, so it is priced by calendar dates — the
/// same 7/31 a fresh signup on the same day would pay. Netting one against the other is what makes
/// the change cost what the difference is worth.
/// </remarks>
public sealed class CalendarAlignedPlanChangeTests
{
    /// <summary>25 August, part-way through a 10th-to-10th anniversary period.</summary>
    private static readonly DateTime Now = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_downgrade_onto_a_calendar_price_banks_the_difference_as_credit()
    {
        var outcome = Calculate(targetUnitAmountMinor: 12_000);

        // Leaving: 16 of the 31 days from 10 August are unused, so 8900 x 16/31 = 4593.
        // Arriving: 25 August to 1 September is 7 of 31 dates, so 12000 x 7/31 = 2710.
        outcome.ChargeMinor.Should().Be(0);
        outcome.NewCreditBalanceMinor.Should().Be(1_883, "4593 unused against a 2710 stub");
    }

    [Fact]
    public void An_upgrade_onto_a_calendar_price_charges_only_the_difference_now()
    {
        var outcome = Calculate(targetUnitAmountMinor: 40_000);

        // 40000 x 7/31 = 9032 for the stub, less the 4593 of unused time being given back.
        outcome.ChargeMinor.Should().Be(4_439);
        outcome.NewCreditBalanceMinor.Should().Be(0);
    }

    /// <summary>
    /// The stub must be priced once. It already runs from now to the next boundary, so scaling it
    /// again by the time left in it would charge a fraction of a fraction.
    /// </summary>
    [Fact]
    public void The_target_stub_is_not_prorated_twice()
    {
        var byCalendarDays = Calculate(targetUnitAmountMinor: 40_000);
        var withoutAFraction = Calculate(
            targetUnitAmountMinor: 40_000,
            fraction: new BillingDayFraction(0, 0));

        // Without a fraction the same target period is scaled by elapsed ticks instead, and the
        // whole month is charged because the period starts now. The two must not agree, or this
        // test is not exercising the calendar path at all.
        byCalendarDays.ChargeMinor.Should().NotBe(withoutAFraction.ChargeMinor);
        byCalendarDays.ChargeMinor.Should().BeLessThan(withoutAFraction.ChargeMinor,
            "seven dates of a month cost less than the month");
    }

    /// <summary>
    /// A change landing on the first buys a whole month, and must pay for a whole month.
    /// </summary>
    /// <remarks>
    /// The fraction is 30/30 rather than absent, which is the distinction that matters: absent
    /// means "price this by the clock", and the clock would charge a subscriber who moved at noon
    /// less than one who signed up fresh at noon for the identical month. Calendar dates decide,
    /// and the time of day is not one of them.
    /// </remarks>
    [Theory]
    // Midnight on the first, and midday on the first. The target month is the same month.
    [InlineData(0)]
    [InlineData(12)]
    public void A_change_on_the_local_first_pays_for_the_whole_target_month(int hourOfDay)
    {
        // An outgoing period that ends exactly on the boundary, so nothing is credited back and
        // the charge is the target's own price and nothing else.
        var subscription = Subscription();
        subscription.CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
            new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = 40_000,
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                BillingAlignment = BillingAlignment.CalendarMonth
            },
            [],
            new DateTime(2026, 9, 1, hourOfDay, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new BillingDayFraction(30, 30));

        outcome.ChargeMinor.Should().Be(40_000,
            "half a day having elapsed does not make it eleven-twelfths of a month");
    }

    [Fact]
    public void A_fixed_discount_shrinks_with_the_target_stub()
    {
        var subscription = Subscription();
        subscription.Discount = new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.FixedAmount,
            AmountMinor = 1_000
        };

        var outcome = Calculate(targetUnitAmountMinor: 40_000, subscription: subscription);

        // The stub's gross is 9032; 7/31 of the 1000 discount is 226, leaving 8806. The outgoing
        // side is discounted by the whole 1000 because it is a whole period: (8900 - 1000) x 16/31
        // = 4077. 8806 - 4077 = 4729.
        outcome.ChargeMinor.Should().Be(4_729);
    }

    private static ProrationOutcome Calculate(
        long targetUnitAmountMinor,
        BillingDayFraction? fraction = null,
        SubscriptionDetail? subscription = null) =>
        SubscriptionProrationCalculator.Calculate(
            subscription ?? Subscription(),
            new PlanSnapshot { Code = "scale", DisplayName = "Scale" },
            new PriceSnapshot
            {
                CurrencyCode = "CHF",
                UnitAmountMinor = targetUnitAmountMinor,
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                BillingAlignment = BillingAlignment.CalendarMonth
            },
            [],
            Now,
            Now,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            fraction ?? new BillingDayFraction(7, 31));

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = "sub-1",
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 8_900,
            Interval = BillingInterval.Month,
            IntervalCount = 1
        },
        // A 31-day anniversary period, 16 days of which are still unused on 25 August.
        CurrentPeriodStartUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };
}
