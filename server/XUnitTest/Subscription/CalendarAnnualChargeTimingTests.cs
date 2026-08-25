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
/// The three ways a yearly price can be sold, priced from one signup on 25 August.
/// </summary>
/// <remarks>
/// Tier 2: CHF 950 a month, CHF 11,400 a year authored independently, 8% off for paying annually.
/// The annual amount is deliberately not derived from the monthly one — what a year costs is a
/// commercial decision, and an annual plan is usually not twelve monthly ones.
/// <para>
/// All three charge the same subscriber for the same plan on the same day. What differs is when the
/// year is collected and when it starts, which is the whole of the configuration.
/// </para>
/// </remarks>
public sealed class CalendarAnnualChargeTimingTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";
    private const long MonthlyMinor = 95_000;
    private const long AnnualMinor = 1_140_000;

    /// <summary>CHF 10,488.00 — the annual amount less its 8% automatic discount.</summary>
    private const long DiscountedAnnualMinor = 1_048_800;

    /// <summary>CHF 197.36 — seven of August's thirty-one dates of the monthly price, less 8%.</summary>
    private const long StubMinor = 19_736;

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();

    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));

    private Price _price = CalendarPrice(CalendarAnnualChargeTiming.AtBoundary);
    private SubscriptionDetail? _created;

    public CalendarAnnualChargeTimingTests()
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
            .Setup(repository => repository.GetOrCreateAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>((subscription, _) => _created = subscription)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Anniversary_collects_the_year_now_and_starts_it_now()
    {
        _price = AnniversaryPrice();

        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(DiscountedAnnualMinor);
        _created.InitialChargeProrated.Should().BeFalse();
        _created.PendingAnnualPeriod.Should().BeNull("the year starts today, so nothing is pending");
        _created.CurrentPeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc));
        _created.CurrentPeriodEndUtc.Should().Be(
            BillingLocalTime.ToUtc(new DateTime(2027, 8, 25, 11, 30, 0), Zone()));
    }

    [Fact]
    public async Task At_boundary_collects_the_stub_now_and_leaves_the_year_owed()
    {
        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(StubMinor,
            "only the stub is collected at checkout");
        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1));

        var pending = _created.PendingAnnualPeriod;
        pending.Should().NotBeNull();
        pending!.StartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        pending.EndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        pending.AmountMinor.Should().Be(DiscountedAnnualMinor);
        pending.IsPrepaid.Should().BeFalse("a year nobody has started is a year nobody has paid for");
    }

    [Fact]
    public async Task At_checkout_collects_the_stub_and_the_year_together()
    {
        _price = CalendarPrice(CalendarAnnualChargeTiming.AtCheckout);

        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(StubMinor + DiscountedAnnualMinor,
            "CHF 197.36 for the stub and CHF 10,488.00 for the year, in one charge");

        var pending = _created.PendingAnnualPeriod;
        pending!.IsPrepaid.Should().BeTrue();
        pending.AmountMinor.Should().Be(DiscountedAnnualMinor);
        pending.StartUtc.Should().Be(LocalMidnight(2026, 9, 1));
        pending.EndUtc.Should().Be(LocalMidnight(2027, 9, 1));

        _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 9, 1),
            "the stub is still the period they hold — the year has not started");
    }

    /// <summary>
    /// The two calendar modes charge different totals now and identical totals overall. That is the
    /// only difference between them, and it is worth asserting as one statement.
    /// </summary>
    [Fact]
    public async Task Both_calendar_modes_come_to_the_same_money()
    {
        await Subscribe();
        var atBoundaryTotal = _created!.InitialChargeAmountMinor +
            _created.PendingAnnualPeriod!.AmountMinor;

        _price = CalendarPrice(CalendarAnnualChargeTiming.AtCheckout);
        await Subscribe();

        _created!.InitialChargeAmountMinor.Should().Be(atBoundaryTotal);
        _created.PendingAnnualPeriod!.AmountMinor.Should().Be(DiscountedAnnualMinor,
            "the year is still worth what it was worth; it has only been paid for early");
    }

    [Fact]
    public async Task The_annual_amount_is_authored_rather_than_twelve_monthly_ones()
    {
        // Priced deliberately below twelve months, which is the ordinary reason to sell a year.
        _price.UnitAmountMinor = 1_000_000;

        await Subscribe();

        _created!.PendingAnnualPeriod!.GrossAmountMinor.Should().Be(1_000_000);
        _created.InitialChargeAmountMinor.Should().Be(StubMinor,
            "the stub still comes from the monthly price, which has not moved");
    }

    /// <summary>
    /// A promotional code buys a discount on the year. Spending it on the seven days before the
    /// year would exchange a month of the customer's promotion for a week of it.
    /// </summary>
    [Fact]
    public async Task A_promotional_code_reduces_the_year_and_not_the_stub()
    {
        GivenDiscount(new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000,
            DurationPeriods = 1
        });

        await Subscribe(discountCode: "welcome");

        _created!.InitialChargeAmountMinor.Should().Be(StubMinor,
            "the stub is priced without the code");

        var pending = _created.PendingAnnualPeriod!;
        // 10% of the 1140000 gross beats the automatic 8%, so the code wins: 1026000.
        pending.AmountMinor.Should().Be(1_026_000);
        pending.DiscountApplied.Should().BeTrue("the code reduced the year, so it is being used");
    }

    [Fact]
    public async Task A_prepaid_year_carries_its_discount_into_the_single_charge()
    {
        _price = CalendarPrice(CalendarAnnualChargeTiming.AtCheckout);
        GivenDiscount(new DiscountTerms
        {
            Code = "welcome",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 1_000
        });

        await Subscribe(discountCode: "welcome");

        _created!.InitialChargeAmountMinor.Should().Be(StubMinor + 1_026_000);
        _created.InitialChargeDiscountApplied.Should().BeTrue(
            "the code reduced money taken in this charge, so the period is spent");
    }

    [Fact]
    public async Task A_signup_on_the_first_has_no_stub_and_nothing_pending_in_either_mode()
    {
        _time.Advance(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero) - _time.GetUtcNow());

        foreach (var timing in new[]
                 {
                     CalendarAnnualChargeTiming.AtBoundary,
                     CalendarAnnualChargeTiming.AtCheckout
                 })
        {
            _price = CalendarPrice(timing);

            await Subscribe();

            _created!.PendingAnnualPeriod.Should().BeNull(
                $"the year starts today under {timing}, so there is nothing to hold");
            _created.InitialChargeAmountMinor.Should().Be(DiscountedAnnualMinor);
            _created.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        }
    }

    [Fact]
    public async Task A_monthly_calendar_price_never_carries_a_pending_year()
    {
        _price = new Price
        {
            ItemId = "price-yearly",
            TenantId = TenantId,
            PlanId = "plan-2",
            CurrencyCode = "CHF",
            UnitAmountMinor = MonthlyMinor,
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            BillingAlignment = BillingAlignment.CalendarMonth,
            Status = CatalogueStatus.Active
        };

        await Subscribe();

        _created!.PendingAnnualPeriod.Should().BeNull(
            "a month's stub is followed by another month of the same price, not a separate term");
    }

    private async Task Subscribe(string? discountCode = null)
    {
        var result = await Service().CreateAsync(
            new CreateSubscriptionRequest
            {
                PlanCode = "tier-2",
                PriceId = "price-yearly",
                TimeZoneId = Zurich,
                DiscountCode = discountCode,
                Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 1 }]
            },
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

    private static TimeZoneInfo Zone() => TimeZoneInfo.FindSystemTimeZoneById(Zurich);

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(new DateTime(year, month, day, 0, 0, 0), Zone());

    private static Price CalendarPrice(CalendarAnnualChargeTiming timing) => new()
    {
        ItemId = "price-yearly",
        TenantId = TenantId,
        PlanId = "plan-2",
        CurrencyCode = "CHF",
        UnitAmountMinor = AnnualMinor,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-monthly",
        CalendarStubBaseUnitAmountMinor = MonthlyMinor,
        CalendarAnnualChargeTiming = timing,
        AutomaticDiscountBasisPoints = 800,
        Status = CatalogueStatus.Active
    };

    private static Price AnniversaryPrice()
    {
        var price = CalendarPrice(CalendarAnnualChargeTiming.AtBoundary);
        price.BillingAlignment = BillingAlignment.Anniversary;
        price.CalendarStubBasePriceId = null;
        price.CalendarStubBaseUnitAmountMinor = null;

        return price;
    }

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
