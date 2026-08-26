using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Subscribing to a calendar-aligned price: what the opening period is, what it costs, and what
/// the subscription is left pointing at afterwards.
/// </summary>
/// <remarks>
/// Driven through the creation service rather than the calculator, because the interesting part is
/// the agreement between three things that are decided separately — the stub the subscriber is
/// entitled to, the fraction they are charged for, and the schedule their renewals derive from.
/// Any two of those can be right while the third quietly disagrees.
/// </remarks>
public sealed class CalendarAlignedSubscriptionTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();

    /// <summary>25 August 2026, 09:30 UTC — 11:30 in Zurich, so the 25th either way.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));

    private Price _price = CalendarPrice();
    private Plan _plan = NewPlan();
    private SubscriptionDetail? _created;

    public CalendarAlignedSubscriptionTests()
    {
        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _plan);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _price);

        _accounts
            .Setup(repository => repository.GetOrCreateAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>((subscription, _) => _created = subscription)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        _subscriptions
            .Setup(repository => repository.GetIncompleteAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);
    }

    /// <summary>
    /// The stub this fixture's headline case buys — a 25 August signup, 7 of 31 days — previewed
    /// exactly as confirmed, and the boundary at which the quote stops holding.
    /// </summary>
    [Fact]
    public async Task A_preview_of_the_stub_quotes_the_same_fraction_and_states_its_boundary()
    {
        var request = new CreateSubscriptionRequest
        {
            PlanCode = "professional",
            PriceId = "price-1",
            TimeZoneId = Zurich,
            Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 1 }]
        };

        var preview = await Service().PreviewAsync(
            request,
            new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1"),
            "corr-preview",
            CancellationToken.None);

        await Subscribe();

        preview.IsSuccess.Should().BeTrue();
        preview.Value!.Prorated.Should().BeTrue();
        preview.Value.CoveredDays.Should().Be(7);
        preview.Value.TotalDays.Should().Be(31);
        preview.Value.TotalDueNowMinor.Should().Be(_created!.InitialChargeAmountMinor);

        // The fraction is quantized per calendar day — 7/31 today, 6/31 tomorrow — so it moves at
        // the very next local midnight, not at the period boundary a week later. This fixture's
        // clock sits on 25 August, 09:30 UTC = 11:30 in Zurich, so the boundary is 26 August,
        // 00:00 Zurich.
        preview.Value.QuoteValidUntilUtc.Should().Be(LocalMidnight(2026, 8, 26));
    }

    /// <summary>
    /// The headline case, and the one every other rule is a variation of.
    /// </summary>
    [Fact]
    public async Task An_august_25_signup_pays_seven_thirty_firsts_and_renews_on_september_1()
    {
        await Subscribe();

        _created!.CurrentPeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc),
            "entitlement starts when they subscribed, not on the first of the month");
        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _created.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 9, 1));

        _created.InitialChargeProrated.Should().BeTrue();
        _created.ProrationDays.Should().Be(7);
        _created.ProrationTotalDays.Should().Be(31);

        // 8900 * 7 / 31 is 2009.68, to the nearest minor unit 2010.
        _created.InitialChargeAmountMinor.Should().Be(2010);
    }

    [Fact]
    public async Task The_renewal_schedule_runs_first_to_first_from_there_on()
    {
        await Subscribe();

        var schedule = _created!.FeeSchedule;
        schedule.AnchorDayOfMonth.Should().Be(1);
        schedule.AnchorMinutesFromMidnight.Should().Be(0);

        // September, then a short month, then a leap February: all first to first.
        PeriodAt(schedule, new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be((LocalMidnight(2026, 9, 1), LocalMidnight(2026, 10, 1)));
        PeriodAt(schedule, new DateTime(2027, 2, 10, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be((LocalMidnight(2027, 2, 1), LocalMidnight(2027, 3, 1)));
        PeriodAt(schedule, new DateTime(2028, 2, 10, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be((LocalMidnight(2028, 2, 1), LocalMidnight(2028, 3, 1)));
    }

    /// <summary>
    /// The full month after the stub is charged at the full amount — the fraction belongs to the
    /// opening period alone and must not follow the subscription around.
    /// </summary>
    [Fact]
    public async Task The_renewal_after_the_stub_charges_a_whole_month()
    {
        await Subscribe();

        var renewal = SubscriptionAmountCalculator.PeriodAmountMinor(
            _created!,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        renewal.AmountMinor.Should().Be(8900);
        renewal.AmountMinor.Should().BeGreaterThan(_created!.InitialChargeAmountMinor!.Value);
    }

    [Theory]
    // February, in a common year and a leap year.
    [InlineData(2026, 2, 10, 19, 28, 6039)]
    [InlineData(2028, 2, 10, 20, 29, 6138)]
    // A 30-day month.
    [InlineData(2026, 4, 15, 16, 30, 4747)]
    public async Task A_short_month_is_split_by_the_days_it_actually_has(
        int year,
        int month,
        int day,
        int expectedDays,
        int expectedTotalDays,
        long expectedAmountMinor)
    {
        _time.Advance(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        await Subscribe();

        _created!.ProrationDays.Should().Be(expectedDays);
        _created.ProrationTotalDays.Should().Be(expectedTotalDays);
        _created.InitialChargeAmountMinor.Should().Be(expectedAmountMinor);
    }

    [Fact]
    public async Task A_signup_on_the_first_gets_a_whole_month_and_is_not_marked_prorated()
    {
        _time.Advance(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        await Subscribe();

        _created!.InitialChargeProrated.Should().BeFalse();
        _created.ProrationDays.Should().BeNull();
        _created.ProrationTotalDays.Should().BeNull();
        _created.InitialChargeAmountMinor.Should().Be(8900, "they are buying a whole month");
        _created.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1),
            "a full period starts on the boundary, not at the instant they happened to click");
        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// The guarantee that makes a quoted price safe to leave on screen: what the customer was
    /// shown is what settles, however long they take to pay it.
    /// </summary>
    [Fact]
    public async Task The_first_charge_is_frozen_when_the_checkout_is_created()
    {
        await Subscribe();

        var quoted = _created!.InitialChargeAmountMinor;

        // They come back the following morning and pay. Nothing about the subscription changed,
        // but the calendar did.
        _time.Advance(TimeSpan.FromDays(1));

        _created.InitialChargeAmountMinor.Should().Be(quoted);
        _created.ProrationDays.Should().Be(7,
            "the frozen fraction describes the period they bought, not the one today would sell");
        CalendarBillingAlignment.FrozenFraction(_created).Apply(8900).Should().Be(2010);
    }

    [Fact]
    public async Task Quantity_pricing_is_prorated_after_the_units_are_multiplied()
    {
        _price.QuantityItemKey = "seat";

        await Subscribe(quantity: 12);

        // 8900 * 12 = 106800 for a whole month; 106800 * 7 / 31 is 24116.13, so 24116.
        _created!.InitialChargeAmountMinor.Should().Be(24116);
    }

    [Fact]
    public async Task Tax_is_charged_on_the_prorated_amount_not_the_whole_month()
    {
        _price.TaxRateBasisPoints = 770;
        _price.TaxMode = TaxMode.Exclusive;

        await Subscribe();

        // 7/31 of 8900 is 2010; 7.7% of that is 154.77, so 155 of tax on top.
        _created!.InitialChargeAmountMinor.Should().Be(2165);
    }

    /// <summary>
    /// A fixed discount has to shrink with the period. Left whole, "50 off" against a seven-day
    /// stub would take more than a quarter of the month's list price off a quarter of a month.
    /// </summary>
    [Fact]
    public async Task A_fixed_discount_is_prorated_by_the_same_day_fraction()
    {
        GivenDiscount(new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.FixedAmount,
            AmountMinor = 1000
        });

        await Subscribe(discountCode: "welcome");

        // 7/31 of 8900 is 2010, and 7/31 of the 1000 discount is 226. 2010 - 226 = 1784.
        _created!.InitialChargeAmountMinor.Should().Be(1784);
    }

    [Fact]
    public async Task A_percentage_discount_applies_to_the_prorated_gross()
    {
        GivenDiscount(new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_000
        });

        await Subscribe(discountCode: "welcome");

        // 20% off the prorated 2010 is 1608.
        _created!.InitialChargeAmountMinor.Should().Be(1608);
    }

    /// <summary>
    /// Metering keeps its own cadence. A subscriber who joins on the 25th gets the plan's whole
    /// allowance for the days they are there, and their usage window is not dragged onto the first.
    /// </summary>
    [Fact]
    public async Task The_usage_schedule_is_left_on_its_own_cadence_and_the_allowance_stays_whole()
    {
        await Subscribe();

        _created!.UsageSchedule.AnchorDayOfMonth.Should().Be(25,
            "usage is metered on the plan's independent schedule, not on the fee's boundary");

        // The two windows open together — both start when the subscription does — but they close
        // a week apart, which is the whole point: the fee stub ends on the first while the meter
        // runs its full month.
        _created.CurrentUsagePeriodEndUtc.Should().Be(
            BillingLocalTime.ToUtc(new DateTime(2026, 9, 25, 11, 30, 0), Zone()));
        _created.CurrentUsagePeriodEndUtc.Should().NotBe(_created.CurrentPeriodEndUtc,
            "the meter must not be dragged onto the fee's calendar boundary");
        _created.Plan.Meters[0].IncludedQuantity.Should().Be(500,
            "an allowance is capacity for a period, not money to be prorated");
    }

    /// <summary>
    /// A card-free trial charges nothing at signup, and what its first paid period will cost
    /// depends on a date that has not arrived. Recording today's fraction would describe a charge
    /// nobody made — and describe it wrongly, since the trial ends in a different part of the month.
    /// </summary>
    [Fact]
    public async Task A_payment_free_trial_records_no_first_charge_at_signup()
    {
        _time.Advance(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = false;

        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().BeNull();
        _created.InitialChargeProrated.Should().BeFalse();
        _created.InitialChargeDiscountApplied.Should().BeFalse();
        _created.ProrationDays.Should().BeNull(
            "a 6 August signup would say 26/31 while the trial ends on the 20th and pays 12/31");
        _created.ProrationTotalDays.Should().BeNull();
    }

    /// <summary>
    /// A trial that takes a card is charged up front, so its first period is priced now like any
    /// other signup.
    /// </summary>
    [Fact]
    public async Task A_payment_required_trial_still_freezes_its_first_charge()
    {
        _plan.TrialDays = 14;
        _plan.TrialRequiresPaymentMethod = true;

        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(2010);
        _created.ProrationDays.Should().Be(7);
    }

    /// <summary>
    /// Whether a promotion reduced the first charge is frozen with the amount, because the answer
    /// depends on the clock and activation can happen long after the money moved.
    /// </summary>
    [Fact]
    public async Task Whether_a_discount_reduced_the_first_charge_is_frozen_with_it()
    {
        GivenDiscount(new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 2_000,
            DurationPeriods = 3,
            ExpiresAtUtc = _time.GetUtcNow().UtcDateTime.AddHours(2)
        });

        await Subscribe(discountCode: "welcome");

        _created!.InitialChargeAmountMinor.Should().Be(1608);
        _created.InitialChargeDiscountApplied.Should().BeTrue(
            "the promotion was live when the charge was raised, whenever it is finally settled");
    }

    [Fact]
    public async Task An_undiscounted_first_charge_says_so()
    {
        await Subscribe();

        _created!.InitialChargeDiscountApplied.Should().BeFalse();
    }

    [Fact]
    public async Task An_anniversary_price_is_completely_unaffected()
    {
        _price = AnniversaryPrice();

        await Subscribe();

        _created!.InitialChargeProrated.Should().BeFalse();
        _created.ProrationDays.Should().BeNull();
        _created.InitialChargeAmountMinor.Should().Be(8900);
        _created.FeeSchedule.AnchorDayOfMonth.Should().Be(25,
            "an anniversary subscription still renews on the day it was bought");
        _created.CurrentPeriodEndUtc.Should().Be(
            BillingLocalTime.ToUtc(new DateTime(2026, 9, 25, 11, 30, 0), Zone()),
            "25 August renews 25 September");
    }

    [Fact]
    public async Task A_calendar_alignment_on_a_cadence_that_cannot_carry_one_is_ignored_at_signup()
    {
        // A quarterly price whose stored alignment says calendar — only reachable if it was
        // written by something that bypassed validation, and it must still bill quarterly.
        _price.IntervalCount = 3;

        await Subscribe();

        _created!.InitialChargeProrated.Should().BeFalse();
        _created.FeeSchedule.IntervalCount.Should().Be(3);
        _created.FeeSchedule.AnchorDayOfMonth.Should().Be(25);
    }

    private async Task Subscribe(long quantity = 1, string? discountCode = null)
    {
        var request = new CreateSubscriptionRequest
        {
            PlanCode = "professional",
            PriceId = "price-1",
            TimeZoneId = Zurich,
            DiscountCode = discountCode,
            Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = quantity }]
        };

        var result = await Service().CreateAsync(
            request,
            new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1"),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "creation should succeed");
    }

    private void GivenDiscount(DiscountTerms terms) =>
        _discounts
            .Setup(repository => repository.FindActiveByCodeAsync(
                TenantId, OrganizationId, terms.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Discount
            {
                TenantId = TenantId,
                Code = terms.Code,
                CurrencyCode = "CHF",
                Terms = terms
            });

    private SubscriptionCreationService Service() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _discounts.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time);

    private static (DateTime StartUtc, DateTime EndUtc) PeriodAt(
        BillingSchedule schedule,
        DateTime instantUtc)
    {
        BillingPeriodCalculator.TryGetPeriod(schedule, instantUtc, out var period)
            .Should().BeTrue();

        return (period.StartUtc, period.EndUtc);
    }

    private static TimeZoneInfo Zone() => TimeZoneInfo.FindSystemTimeZoneById(Zurich);

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(new DateTime(year, month, day, 0, 0, 0), Zone());

    private static Price CalendarPrice() => new()
    {
        ItemId = "price-1",
        TenantId = TenantId,
        PlanId = "plan-1",
        CurrencyCode = "CHF",
        UnitAmountMinor = 8900,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        Status = CatalogueStatus.Active
    };

    private static Price AnniversaryPrice()
    {
        var price = CalendarPrice();
        price.BillingAlignment = BillingAlignment.Anniversary;

        return price;
    }

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-1",
        TenantId = TenantId,
        Code = "professional",
        DisplayName = "Professional",
        Status = CatalogueStatus.Active,
        Version = 3,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ],
        Meters =
        [
            new PlanMeter
            {
                MeterKey = "screening",
                UnitLabel = "screening",
                IncludedQuantity = 500
            }
        ]
    };
}
