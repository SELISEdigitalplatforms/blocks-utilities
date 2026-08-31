using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A subscription as its owner reads it back.
/// </summary>
/// <remarks>
/// What matters here is what the ordinary read carries. A quantity change that is only visible in
/// the response to the request that made it is invisible after a page reload, which leaves a
/// client showing the quantity in force with nothing to say a different one is already booked.
/// </remarks>
public sealed class SubscriptionResponseMapperTests
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SubscriptionResponseMapper _mapper = new(
        new ControlledTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_scheduled_decrease_is_carried_on_the_ordinary_read()
    {
        var subscription = NewSubscription(10);
        subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities = [Item(9)],
            RequestedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            EffectiveAtUtc = PeriodEnd
        };

        var response = _mapper.ToResponse(subscription);

        response.Quantities.Single().Quantity.Should().Be(10, "the paid quantity still stands");
        response.PendingQuantityChange!.Quantities.Single().Quantity.Should().Be(9);
        response.PendingQuantityChange.EffectiveAtUtc.Should().Be(PeriodEnd);
    }

    [Fact]
    public void A_subscription_with_nothing_scheduled_says_so()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        response.PendingQuantityChange.Should().BeNull();
    }

    [Fact]
    public void The_band_and_the_recurring_amount_are_stated_rather_than_left_to_be_derived()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        // 10 users at CHF 145 with the 10% band: CHF 1,305.00.
        response.CurrentTier!.DiscountBasisPoints.Should().Be(1_000);
        response.RecurringAmountMinor.Should().Be(130_500);
        response.UnitAmountMinor.Should().Be(
            14_500,
            "the unit amount stays what the price says, undiscounted");
    }

    [Fact]
    public void Usage_cadence_is_reported_independently_of_billing_cadence()
    {
        // A plan is free to bill yearly and meter monthly -- nothing ties the two together, so
        // the response must not either. Reading UsageInterval from Price (as Interval is) would
        // have reported "Year" for a meter that actually resets every month.
        var subscription = NewSubscription(10);
        subscription.Price.Interval = BillingInterval.Year;
        subscription.Price.IntervalCount = 1;
        subscription.Plan.UsageInterval = BillingInterval.Month;
        subscription.Plan.UsageIntervalCount = 1;

        var response = _mapper.ToResponse(subscription);

        response.Interval.Should().Be("Year");
        response.UsageInterval.Should().Be("Month",
            "the meter resets monthly regardless of how often the fee itself is billed");
        response.UsageIntervalCount.Should().Be(1);
    }

    [Fact]
    public void A_subscription_nobody_has_cancelled_carries_no_cancellation()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        response.Cancellation.Should().BeNull();
        response.CancelAtPeriodEnd.Should().BeFalse();
        response.CanceledAtUtc.Should().BeNull();
    }

    [Fact]
    public void A_scheduled_cancellation_reports_the_request_and_period_end_separately()
    {
        var subscription = NewSubscription(10);
        subscription.CancelAtPeriodEnd = true;
        subscription.CanCancelImmediately = true;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation.Should().NotBeNull();
        response.Cancellation!.State.Should().Be("Scheduled");
        response.Cancellation.RequestedAtUtc.Should().Be(subscription.CanceledAtUtc.Value);
        response.Cancellation.EffectiveAtUtc.Should().Be(PeriodEnd,
            "access continues to the paid period's end while the cancellation is only scheduled");
        response.Cancellation.CanCancelImmediately.Should().BeTrue();

        // Legacy fields keep reporting the same facts for clients that have not moved over.
        response.CancelAtPeriodEnd.Should().BeTrue();
        response.CanceledAtUtc.Should().Be(subscription.CanceledAtUtc);
    }

    [Fact]
    public void A_schedule_locked_to_a_prepaid_annual_term_reports_that_it_cannot_be_escalated()
    {
        var subscription = NewSubscription(10);
        subscription.CancelAtPeriodEnd = true;
        subscription.CanCancelImmediately = false;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation!.CanCancelImmediately.Should().BeFalse();
    }

    [Fact]
    public void A_prepaid_pending_year_reports_its_end_as_the_next_payment()
    {
        var subscription = NewSubscription(10);
        subscription.NextFeeBillingAtUtc = PeriodEnd;
        subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = PeriodEnd,
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = true
        };

        var response = _mapper.ToResponse(subscription);

        response.NextPaymentAtUtc.Should().Be(subscription.PendingAnnualPeriod.EndUtc,
            "the earlier worker boundary opens an annual term that has already been paid for");
    }

    [Fact]
    public void An_unpaid_pending_year_reports_its_start_as_the_next_payment()
    {
        var subscription = NewSubscription(10);
        subscription.NextFeeBillingAtUtc = PeriodEnd;
        subscription.PendingAnnualPeriod = new PendingAnnualPeriod
        {
            StartUtc = PeriodEnd,
            EndUtc = new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrepaid = false
        };

        var response = _mapper.ToResponse(subscription);

        response.NextPaymentAtUtc.Should().Be(PeriodEnd,
            "a year configured for boundary collection is still owed when it starts");
    }

    [Fact]
    public void An_effective_cancellation_reports_when_access_actually_ended()
    {
        var subscription = NewSubscription(10);
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);
        subscription.EndedAtUtc = new DateTime(2026, 8, 16, 11, 5, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation!.State.Should().Be("Effective");
        response.Cancellation.EffectiveAtUtc.Should().Be(subscription.EndedAtUtc.Value,
            "once cancellation has taken effect, EffectiveAtUtc is when it actually did — not " +
            "the period boundary it would otherwise have waited for");
    }

    [Fact]
    public void A_priced_meter_returns_the_matching_snapshotted_rate_table()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        subscription.Plan.Meters = [Meter("screening", (100, "1.00"), (null, "0.80"))];

        var response = PricedMapper().ToResponse(subscription);

        var meter = response.Meters.Single();
        meter.OverageAllowed.Should().BeTrue();
        meter.OveragePricing.Should().NotBeNull();
        meter.OveragePricing!.CurrencyCode.Should().Be("CHF");
        meter.OveragePricing.Tiers.Should().HaveCount(2);
        meter.OveragePricing.Tiers[0].UpToQuantity.Should().Be(100);
        meter.OveragePricing.Tiers[0].UnitAmount.Should().Be("1.00");
        meter.OveragePricing.Tiers[1].UpToQuantity.Should().BeNull();
        meter.OveragePricing.Tiers[1].UnitAmount.Should().Be("0.80");
    }

    [Theory]
    [InlineData("CHF", 2, 100, "1.00")]
    [InlineData("JPY", 0, 100, "100")]
    [InlineData("KWD", 3, 100, "0.100")]
    public void Major_unit_conversion_covers_zero_two_and_three_decimal_currencies(
        string currencyCode, int decimals, long unitAmountMinor, string expected)
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = currencyCode;
        subscription.Plan.Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                DisplayName = "Screenings",
                UnitLabel = "screening",
                IncludedQuantity = 150,
                OverageAllowed = true,
                RateTables =
                [
                    new MeterRateTable
                    {
                        CurrencyCode = currencyCode,
                        Tiers = [new MeterTier { UpToQuantity = null, UnitAmountMinor = unitAmountMinor }]
                    }
                ]
            }
        ];

        var response = PricedMapper(decimalsByCurrency: new() { [currencyCode] = decimals })
            .ToResponse(subscription);

        response.Meters.Single().OveragePricing!.Tiers.Single().UnitAmount.Should().Be(expected);
    }

    [Fact]
    public void Blocked_overage_returns_no_pricing_even_with_a_rate_table_present()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        var meter = Meter("screening", (null, "1.00"));
        meter.OverageAllowed = false;
        subscription.Plan.Meters = [meter];

        var response = PricedMapper().ToResponse(subscription);

        var mapped = response.Meters.Single();
        mapped.OverageAllowed.Should().BeFalse();
        mapped.OveragePricing.Should().BeNull();
    }

    [Fact]
    public void Overage_allowed_with_no_rate_table_is_distinguishable_from_blocked_overage()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        subscription.Plan.Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                DisplayName = "Screenings",
                UnitLabel = "screening",
                IncludedQuantity = 150,
                OverageAllowed = true,
                RateTables = []
            }
        ];

        var response = PricedMapper().ToResponse(subscription);

        var mapped = response.Meters.Single();
        mapped.OverageAllowed.Should().BeTrue("blocked and unpriced must read differently to a client");
        mapped.OveragePricing.Should().BeNull();
    }

    [Fact]
    public void A_rate_table_for_another_currency_is_never_exposed()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        var meter = Meter("screening", (null, "1.00"));
        meter.RateTables[0].CurrencyCode = "EUR";
        subscription.Plan.Meters = [meter];

        var response = PricedMapper().ToResponse(subscription);

        response.Meters.Single().OveragePricing.Should().BeNull(
            "the subscription is priced in CHF; a EUR-only rate table prices nothing for it");
    }

    [Fact]
    public void Multiple_meters_are_mapped_independently()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        var blocked = Meter("blocked-thing", (null, "1.00"));
        blocked.OverageAllowed = false;
        subscription.Plan.Meters =
        [
            Meter("screening", (100, "1.00"), (null, "0.80")),
            blocked
        ];

        var response = PricedMapper().ToResponse(subscription);

        response.Meters.Should().HaveCount(2);
        response.Meters.Single(meter => meter.MeterKey == "screening")
            .OveragePricing!.Tiers.Should().HaveCount(2);
        response.Meters.Single(meter => meter.MeterKey == "blocked-thing")
            .OveragePricing.Should().BeNull();
    }

    [Fact]
    public void A_legacy_subscription_with_no_meters_returns_an_empty_list()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        response.Meters.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void A_mapper_with_no_currency_resolver_wired_leaves_priced_meters_unpriced_rather_than_guessing()
    {
        var subscription = NewSubscription(10);
        subscription.CurrencyCode = "CHF";
        subscription.Plan.Meters = [Meter("screening", (null, "1.00"))];

        // The default mapper -- exactly the one every other test in this file already uses --
        // never wires a currency resolver. It must still answer the endpoint, just without a
        // fabricated price.
        var response = _mapper.ToResponse(subscription);

        var mapped = response.Meters.Single();
        mapped.OverageAllowed.Should().BeTrue();
        mapped.OveragePricing.Should().BeNull();
    }

    private static PlanMeter Meter(string meterKey, params (long? upTo, string amount)[] tiers) =>
        new()
        {
            MeterKey = meterKey,
            DisplayName = meterKey,
            UnitLabel = "unit",
            IncludedQuantity = 150,
            ResetPolicy = MeterResetPolicy.Periodic,
            OverageAllowed = true,
            RateTables =
            [
                new MeterRateTable
                {
                    CurrencyCode = "CHF",
                    Tiers = tiers
                        .Select(tier => new MeterTier
                        {
                            UpToQuantity = tier.upTo,
                            // Parsed back from the display string at 2 decimals -- only the
                            // dedicated major-unit conversion test overwrites this with a minor
                            // amount matching a different currency's precision.
                            UnitAmountMinor = (long)(decimal.Parse(
                                tier.amount,
                                System.Globalization.CultureInfo.InvariantCulture) * 100)
                        })
                        .ToList()
                }
            ]
        };

    private static SubscriptionResponseMapper PricedMapper(
        Dictionary<string, int>? decimalsByCurrency = null)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options
            .SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions
            {
                CurrencyMinorUnits = new Dictionary<string, int>(
                    decimalsByCurrency ?? new Dictionary<string, int> { ["CHF"] = 2 },
                    StringComparer.OrdinalIgnoreCase)
            });

        ICurrencyMinorUnitResolver resolver = new CurrencyMinorUnitResolver(options.Object);

        return new SubscriptionResponseMapper(
            new ControlledTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)),
            resolver);
    }

    private static SubscriptionQuantityItem Item(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = 14_500
    };

    private static SubscriptionDetail NewSubscription(long quantity) => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = PeriodStart,
        CurrentPeriodEndUtc = PeriodEnd,
        QuantityItems = [Item(quantity)],
        Price = new PriceSnapshot
        {
            UnitAmountMinor = 14_500,
            CurrencyCode = "CHF",
            QuantityItemKey = "user",
            Interval = BillingInterval.Month,
            IntervalCount = 1
        },
        Plan = new PlanSnapshot
        {
            Code = "team",
            DisplayName = "Team",
            QuantityItems =
            [
                new PlanQuantityItem
                {
                    ItemKey = "user",
                    UnitLabel = "user",
                    MinQuantity = 1,
                    QuantityDiscountTiers =
                    [
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 1,
                            MaximumQuantity = 9,
                            DiscountBasisPoints = 0
                        },
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 10,
                            MaximumQuantity = null,
                            DiscountBasisPoints = 1_000
                        }
                    ]
                }
            ]
        }
    };
}
