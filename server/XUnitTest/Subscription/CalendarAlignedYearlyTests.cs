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
/// A yearly price aligned to the calendar: a stub measured in days, then twelve whole months.
/// </summary>
/// <remarks>
/// The stub is the reason this is not simply the monthly feature with a different interval. Days of
/// an annual amount are not a quantity anyone can charge, so the opening period is priced from the
/// monthly price the annual one was linked to — and the annual cycle does not begin until the
/// first, or a year bought on 25 August would end on 1 August.
/// <para>
/// Worked against Tier 2: CHF 950 a month, 8% off for paying annually.
/// </para>
/// </remarks>
public sealed class CalendarAlignedYearlyTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";

    /// <summary>CHF 950.00 a month, so CHF 11,400.00 a year before the annual discount.</summary>
    private const long MonthlyMinor = 95_000;

    private const long YearlyGrossMinor = MonthlyMinor * 12;

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();

    /// <summary>25 August 2026, 09:30 UTC — 11:30 in Zurich, so the 25th either way.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));

    private Price _price = YearlyPrice();
    private SubscriptionDetail? _created;

    public CalendarAlignedYearlyTests()
    {
        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "tier-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPlan);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-yearly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _price);

        _accounts
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>((subscription, _) => _created = subscription)
            .ReturnsAsync(true);
    }

    /// <summary>
    /// The worked example: CHF 950 x 7/31 x 92% for the stub, then a full year from 1 September.
    /// </summary>
    [Fact]
    public async Task An_august_25_signup_pays_a_monthly_stub_then_starts_its_year_on_september_1()
    {
        await Subscribe();

        _created!.CurrentPeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc));
        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _created.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2026, 9, 1));

        _created.InitialChargeProrated.Should().BeTrue();
        _created.ProrationDays.Should().Be(7);
        _created.ProrationTotalDays.Should().Be(31);

        // 95000 x 7/31 is 21451.6, so 21452; 8% off that is 1716 (truncated), leaving 19736.
        _created.InitialChargeAmountMinor.Should().Be(19_736,
            "CHF 197.36 — a week of the monthly price, at the annual plan's discount");
    }

    [Fact]
    public async Task The_annual_cycle_runs_first_to_first_a_year_apart()
    {
        await Subscribe();

        var schedule = _created!.FeeSchedule;
        schedule.Interval.Should().Be(BillingInterval.Year);
        schedule.IntervalCount.Should().Be(1);
        schedule.AnchorDayOfMonth.Should().Be(1);
        schedule.AnchorMinutesFromMidnight.Should().Be(0);

        // The year opens on 1 September and closes on 1 September, not on 1 August.
        PeriodAt(schedule, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be((LocalMidnight(2026, 9, 1), LocalMidnight(2027, 9, 1)));
        PeriodAt(schedule, new DateTime(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be((LocalMidnight(2027, 9, 1), LocalMidnight(2028, 9, 1)));
    }

    /// <summary>
    /// The full annual charge, which is the derived twelve months rather than anything prorated.
    /// </summary>
    [Fact]
    public async Task The_charge_on_the_first_is_the_whole_discounted_year()
    {
        await Subscribe();

        var annual = SubscriptionAmountCalculator.PeriodAmountMinor(
            _created!,
            LocalMidnight(2026, 9, 1));

        // 1140000 less 8% is 1048800: CHF 10,488.00.
        annual.AmountMinor.Should().Be(1_048_800);
    }

    /// <summary>
    /// A signup already on the first buys a year from that day, with no stub at all.
    /// </summary>
    [Fact]
    public async Task A_signup_on_the_local_first_starts_its_year_immediately()
    {
        _time.Advance(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        await Subscribe();

        _created!.InitialChargeProrated.Should().BeFalse();
        _created.ProrationDays.Should().BeNull();
        _created.InitialChargeAmountMinor.Should().Be(1_048_800, "a whole discounted year");
        _created.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1),
            "a year, not the month the calendar-month rule would have given a monthly price");
    }

    [Theory]
    // February in a common year, then a leap year, then a 30-day month.
    // 95000 x 19/28 is 64464; less 8% (5157) is 59307.
    [InlineData(2026, 2, 10, 19, 28, 59_307)]
    // The leap day changes the denominator: 95000 x 20/29 is 65517, less 8% (5241) is 60276.
    [InlineData(2028, 2, 10, 20, 29, 60_276)]
    // 95000 x 16/30 is 50667; less 8% (4053) is 46614.
    [InlineData(2026, 4, 15, 16, 30, 46_614)]
    public async Task The_stub_is_split_by_the_days_the_month_actually_has(
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

    /// <summary>
    /// The stub is a fraction of the monthly price, not of a daily rate derived from the annual
    /// one. Those differ, and only one of them is a number the subscriber was shown.
    /// </summary>
    [Fact]
    public async Task The_stub_is_a_fraction_of_the_monthly_price_not_of_the_year()
    {
        await Subscribe();

        var aFractionOfTheYear = CalendarBillingAlignment.Prorate(YearlyGrossMinor, 7, 31);

        _created!.InitialChargeAmountMinor.Should().NotBe(aFractionOfTheYear);
        _created.InitialChargeAmountMinor.Should().BeLessThan(aFractionOfTheYear / 10,
            "a week of a month is roughly a twelfth of a week of a year");
    }

    [Fact]
    public async Task The_monthly_basis_is_snapshotted_onto_the_subscription()
    {
        await Subscribe();

        _created!.Price.CalendarStubBasePriceId.Should().Be("price-monthly");
        _created.Price.CalendarStubBaseUnitAmountMinor.Should().Be(MonthlyMinor,
            "the stub must be reproducible without ever reading the monthly price again");
    }

    /// <summary>
    /// Editing the monthly price afterwards must not move an annual subscriber's stub. The
    /// snapshot is the whole defence, since the stub is charged at checkout and the year a month
    /// later.
    /// </summary>
    [Fact]
    public async Task Repricing_the_monthly_price_afterwards_changes_nothing_already_sold()
    {
        await Subscribe();
        var quoted = _created!.InitialChargeAmountMinor;

        _price.CalendarStubBaseUnitAmountMinor = 500_000;

        SubscriptionAmountCalculator
            .FirstPeriodCharge(_created, new BillingDayFraction(7, 31), _time.GetUtcNow().UtcDateTime)
            .AmountMinor.Should().Be(quoted);
    }

    [Fact]
    public async Task A_quantity_priced_yearly_stub_multiplies_the_monthly_amount()
    {
        _price.QuantityItemKey = "seat";

        await Subscribe(quantity: 4);

        // 4 seats at 95000 is 380000 a month; 7/31 of that is 85806; less 8% is 78942.
        _created!.InitialChargeAmountMinor.Should().Be(78_942);
    }

    /// <summary>
    /// The annual per-seat amount snapshotted on the quantity item is twelve times too much for a
    /// week, so the stub has to be re-expressed at the monthly amount rather than reusing it.
    /// </summary>
    [Fact]
    public async Task A_quantity_priced_stub_does_not_use_the_annual_per_seat_amount()
    {
        _price.QuantityItemKey = "seat";

        await Subscribe(quantity: 4);

        _created!.QuantityItems.Single().UnitAmountMinor.Should().Be(YearlyGrossMinor,
            "the subscriber agreed an annual per-seat amount, and that is what renewals charge");
        _created.InitialChargeAmountMinor.Should().BeLessThan(100_000,
            "but the stub is a week of the monthly equivalent, not a week of the annual one");
    }

    [Fact]
    public async Task An_anniversary_yearly_price_is_completely_unaffected()
    {
        _price.BillingAlignment = BillingAlignment.Anniversary;
        _price.CalendarStubBasePriceId = null;
        _price.CalendarStubBaseUnitAmountMinor = null;

        await Subscribe();

        _created!.InitialChargeProrated.Should().BeFalse();
        _created.InitialChargeAmountMinor.Should().Be(1_048_800);
        _created.FeeSchedule.AnchorDayOfMonth.Should().Be(25);
        _created.CurrentPeriodEndUtc.Should().Be(
            BillingLocalTime.ToUtc(new DateTime(2027, 8, 25, 11, 30, 0), Zone()),
            "25 August renews 25 August the following year");
    }

    /// <summary>
    /// A yearly price whose stub basis never made it onto the snapshot cannot price a stub, so it
    /// must bill as an ordinary anniversary year rather than guess at one.
    /// </summary>
    [Fact]
    public async Task A_yearly_price_with_no_snapshotted_basis_falls_back_to_a_whole_year()
    {
        _price.CalendarStubBaseUnitAmountMinor = null;

        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(1_048_800,
            "with nothing to price a week from, the subscriber buys a year");
    }

    [Fact]
    public async Task The_usage_schedule_keeps_its_own_monthly_cadence()
    {
        await Subscribe();

        _created!.UsageSchedule.Interval.Should().Be(BillingInterval.Month);
        _created.UsageSchedule.AnchorDayOfMonth.Should().Be(25,
            "metering is independent of the fee, and an annual plan still meters monthly");
        _created.Plan.Meters[0].IncludedQuantity.Should().Be(500);
    }

    private async Task Subscribe(long quantity = 1)
    {
        var request = new CreateSubscriptionRequest
        {
            PlanCode = "tier-2",
            PriceId = "price-yearly",
            TimeZoneId = Zurich,
            Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = quantity }]
        };

        var result = await Service().CreateAsync(
            request,
            new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1"),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "creation should succeed");
    }

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

    private static Price YearlyPrice() => new()
    {
        ItemId = "price-yearly",
        TenantId = TenantId,
        PlanId = "plan-2",
        CurrencyCode = "CHF",
        UnitAmountMinor = YearlyGrossMinor,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-monthly",
        CalendarStubBaseUnitAmountMinor = MonthlyMinor,
        AutomaticDiscountBasisPoints = 800,
        Status = CatalogueStatus.Active
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-2",
        TenantId = TenantId,
        Code = "tier-2",
        DisplayName = "Tier 2",
        Status = CatalogueStatus.Active,
        Version = 1,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ],
        Meters =
        [
            new PlanMeter { MeterKey = "screening", UnitLabel = "screening", IncludedQuantity = 500 }
        ]
    };
}
