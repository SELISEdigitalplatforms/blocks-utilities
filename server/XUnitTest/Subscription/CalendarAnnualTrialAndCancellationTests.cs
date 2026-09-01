using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A calendar-aligned yearly subscription reached through a trial, and what cancelling one does.
/// </summary>
/// <remarks>
/// Driven from <c>CreateAsync</c> through to the conversion renewal rather than from a
/// hand-built subscription, because the defect these cover is precisely a disagreement between what
/// signup stores and what conversion needs: a trial that ends in a different month than the signup.
/// </remarks>
public sealed class CalendarAnnualTrialAndCancellationTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string Zurich = "Europe/Zurich";
    private const long MonthlyMinor = 95_000;
    private const long DiscountedAnnualMinor = 1_048_800;

    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionDiscountRepository> _discounts = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();

    /// <summary>25 August 2026 — a signup whose trial will end in a different month.</summary>
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));

    private CalendarAnnualChargeTiming _timing = CalendarAnnualChargeTiming.AtBoundary;

    /// <summary>26 days from 25 August lands on 20 September, mid-month.</summary>
    private int _trialDays = 26;

    /// <summary>37 days from 25 August lands on 1 October, exactly on a boundary.</summary>
    private const int TrialDaysEndingOnOctoberFirst = 37;
    private SubscriptionDetail? _created;
    private SubscriptionTransition? _transition;
    private SubscriptionChargeRequest? _charge;
    private int _chargeCount;

    public CalendarAnnualTrialAndCancellationTests()
    {
        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "tier-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrialPlan);

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-yearly", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => YearlyPrice(_timing));

        _accounts
            .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount account, CancellationToken _) => account);

        _accounts
            .Setup(repository => repository.GetAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                TenantId = TenantId,
                OrganizationId = OrganizationId,
                ProviderName = "STRIPE",
                DefaultPaymentMethodId = "pm-1"
            });

        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _cancelling);

        _subscriptions
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionDetail, CancellationToken>((subscription, _) => _created = subscription)
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, It.IsAny<string>(), It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<SubscriptionChargeRequest, string, string, CancellationToken>(
                (request, _, _, _) =>
                {
                    _charge = request;
                    _chargeCount++;
                })
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
    }

    /// <summary>
    /// The defect this exists for: a 25 August signup on a 26-day trial ends 20 September, so the
    /// year starts 1 October. A year frozen at signup would have said 1 September and skipped the
    /// 20–30 September stub entirely.
    /// </summary>
    [Fact]
    public async Task A_trial_ending_in_a_later_month_holds_no_year_until_it_converts()
    {
        await Subscribe();

        _created!.Trial.Should().NotBeNull();
        _created.PendingAnnualPeriod.Should().BeNull(
            "which month the trial ends in decides when the year starts, and that is not today");
        _created.InitialChargeAmountMinor.Should().BeNull("a card-free trial charges nothing");
    }

    [Fact]
    public async Task The_conversion_prices_the_stub_and_the_year_from_the_trial_end()
    {
        await Subscribe();
        await Convert();

        // 20 September through 30 September is 11 of 30 dates: 95000 x 11/30 is 34833, less 8%
        // (2786) is 32047.
        _charge!.AmountMinor.Should().Be(32_047);
        _transition!.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2026, 10, 1));

        var held = _transition.PendingAnnualPeriod;
        held.Should().NotBeNull();
        held!.StartUtc.Should().Be(LocalMidnight(2026, 10, 1),
            "the year starts after the month the trial ended in, not after the month of signup");
        held.EndUtc.Should().Be(LocalMidnight(2027, 10, 1));
        held.AmountMinor.Should().Be(DiscountedAnnualMinor);
        held.IsPrepaid.Should().BeFalse("this price collects the year at its own boundary");
    }

    /// <summary>
    /// "Collect the year with the first payment" — and for a card-free trial the conversion is the
    /// first payment there has ever been.
    /// </summary>
    [Fact]
    public async Task A_prepaid_trial_conversion_collects_the_stub_and_the_year_together()
    {
        _timing = CalendarAnnualChargeTiming.AtCheckout;

        await Subscribe();
        await Convert();

        _charge!.AmountMinor.Should().Be(32_047 + DiscountedAnnualMinor);

        var held = _transition!.PendingAnnualPeriod;
        held!.IsPrepaid.Should().BeTrue("this charge is the one that collected it");
        held.StartUtc.Should().Be(LocalMidnight(2026, 10, 1));
    }

    /// <summary>
    /// A trial ending on the local first has no stub to buy: the subscriber steps straight into a
    /// whole year. Charging the annual amount for a one-month period and then holding a second year
    /// behind it would bill them twice for the same twelve months.
    /// </summary>
    [Theory]
    [InlineData(CalendarAnnualChargeTiming.AtBoundary)]
    [InlineData(CalendarAnnualChargeTiming.AtCheckout)]
    public async Task A_trial_ending_on_the_first_opens_a_whole_year_and_holds_nothing(
        CalendarAnnualChargeTiming timing)
    {
        _timing = timing;
        _trialDays = TrialDaysEndingOnOctoberFirst;

        await Subscribe();
        await Convert();

        _chargeCount.Should().Be(1);
        _charge!.AmountMinor.Should().Be(DiscountedAnnualMinor,
            "one annual amount, for the year that starts today");

        _transition!.CurrentPeriodStartUtc.Should().Be(LocalMidnight(2026, 10, 1));
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 10, 1),
            "a year, not the month a stub would have given");
        _transition.NextFeeBillingAtUtc.Should().Be(LocalMidnight(2027, 10, 1),
            "the next charge is a year away, not a month");

        _transition.PendingAnnualPeriod.Should().BeNull(
            "the subscriber is inside the year already; a second one would be billed twice");
    }

    [Fact]
    public async Task A_trial_ending_on_the_first_is_not_reported_as_a_prorated_first_charge()
    {
        _trialDays = TrialDaysEndingOnOctoberFirst;

        await Subscribe();
        await Convert();

        _transition!.InitialChargeProrated.Should().BeFalse(
            "nothing about this charge is a fraction of anything");
        _transition.ProrationDays.Should().BeNull();
        _transition.ProrationTotalDays.Should().BeNull();

        // Still recorded, though: a card-free trial leaves these unset at signup, so the conversion
        // is the only place the first paid charge can be accounted for.
        _transition.InitialChargeAmountMinor.Should().Be(DiscountedAnnualMinor);
    }

    /// <summary>
    /// A year already paid for is a year the subscriber keeps, whatever flag the caller sends.
    /// Ending access now would take the money and the entitlement together.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelling_a_prepaid_stub_keeps_access_to_the_end_of_the_year(bool immediately)
    {
        InPrepaidStub();

        var result = await Cancellation().CancelAsync(
            "sub-1", immediately, "not needed", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.NewStatus.Should().NotBe(SubscriptionStatus.Canceled,
            "the subscriber holds a year they paid for");
        _transition.CancelAtPeriodEnd.Should().BeTrue();
        _transition.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1),
            "access runs to the end of the year they bought");
        _transition.ClearPendingAnnualPeriod.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_an_unpaid_stub_ends_with_the_stub_and_never_charges_the_year()
    {
        var subscription = InPrepaidStub();
        subscription.PendingAnnualPeriod!.IsPrepaid = false;
        subscription.PendingAnnualPeriod.CollectedWithCheckout = false;

        await Cancellation().CancelAsync(
            "sub-1", false, "not needed", null, "corr-1", CancellationToken.None);

        _transition!.CancelAtPeriodEnd.Should().BeTrue();
        _transition.CurrentPeriodEndUtc.Should().BeNull(
            "the stub is the last period; there is no year to extend into");
        _transition.ClearPendingAnnualPeriod.Should().BeTrue(
            "so no later sweep can charge for a year this subscription will never hold");
        _transition.ClearNextFeeBillingAt.Should().BeTrue();
    }

    /// <summary>
    /// The response has to describe the write, not the state before it.
    /// </summary>
    [Fact]
    public async Task The_cancellation_response_reflects_what_was_written()
    {
        InPrepaidStub();

        var result = await Cancellation().CancelAsync(
            "sub-1", false, "not needed", null, "corr-1", CancellationToken.None);

        result.Value!.PendingAnnualPeriod.Should().BeNull(
            "a 200 still advertising a pending year would have a client offering to cancel " +
            "something this very write has already dealt with");
        result.Value.CurrentPeriodEndUtc.Should().Be(LocalMidnight(2027, 9, 1));
        result.Value.CancelAtPeriodEnd.Should().BeTrue();
    }

    private async Task Subscribe()
    {
        var result = await Creation().CreateAsync(
            new CreateSubscriptionRequest
            {
                PlanCode = "tier-2",
                PriceId = "price-yearly",
                TimeZoneId = Zurich,
                Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 1 }]
            },
            new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1"),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorCode ?? "creation should succeed");
    }

    /// <summary>Runs the conversion renewal on the subscription signup actually produced.</summary>
    private async Task Convert()
    {
        var subscription = _created!;
        subscription.Status = SubscriptionStatus.Trialing;
        subscription.BillingAccountId = "acct-1";
        _time.Advance(subscription.Trial!.EndsAtUtc - _time.GetUtcNow());

        await Renewal().RenewAsync(subscription, CancellationToken.None);
    }

    private SubscriptionCreationService Creation() => new(
        _catalogue.Object,
        _subscriptions.Object,
        _discounts.Object,
        _accounts.Object,
        new CreateSubscriptionRequestValidator(),
        NullLogger<SubscriptionCreationService>.Instance,
        _time);

    private SubscriptionRenewalService Renewal() => new(
        _subscriptions.Object,
        _accounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time);

    /// <summary>
    /// Built with the real response mapper, so the assertions read the response a caller would
    /// actually receive rather than an intermediate the test agreed with itself about.
    /// </summary>
    private SubscriptionCancellationService Cancellation() => new(
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(_time),
        _accounts.Object,
        _cache.Object,
        NullLogger<SubscriptionCancellationService>.Instance,
        _time);

    private SubscriptionDetail? _cancelling;

    /// <summary>A subscription inside a stub holding a paid year that starts 1 September.</summary>
    private SubscriptionDetail InPrepaidStub()
    {
        _cancelling = new SubscriptionDetail
        {
            ItemId = "sub-1",
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            BillingAccountId = "acct-1",
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot { Code = "tier-2", DisplayName = "Tier 2" },
            Price = SnapshotOf(YearlyPrice(CalendarAnnualChargeTiming.AtCheckout)),
            CurrentPeriodStartUtc = new DateTime(2026, 8, 25, 9, 30, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = LocalMidnight(2026, 9, 1),
            PendingAnnualPeriod = new PendingAnnualPeriod
            {
                StartUtc = LocalMidnight(2026, 9, 1),
                EndUtc = LocalMidnight(2027, 9, 1),
                AmountMinor = DiscountedAnnualMinor,
                CollectedWithCheckout = true,
                IsPrepaid = true
            }
        };

        return _cancelling;
    }

    private static PriceSnapshot SnapshotOf(Price price) => new()
    {
        PriceId = price.ItemId,
        CurrencyCode = price.CurrencyCode,
        UnitAmountMinor = price.UnitAmountMinor,
        Interval = price.Interval,
        IntervalCount = price.IntervalCount,
        BillingAlignment = price.BillingAlignment,
        CalendarStubBasePriceId = price.CalendarStubBasePriceId,
        CalendarStubBaseUnitAmountMinor = price.CalendarStubBaseUnitAmountMinor,
        CalendarAnnualChargeTiming = price.CalendarAnnualChargeTiming,
        AutomaticDiscountBasisPoints = price.AutomaticDiscountBasisPoints
    };

    private static DateTime LocalMidnight(int year, int month, int day) =>
        BillingLocalTime.ToUtc(
            new DateTime(year, month, day, 0, 0, 0),
            TimeZoneInfo.FindSystemTimeZoneById(Zurich));

    private static Price YearlyPrice(CalendarAnnualChargeTiming timing) => new()
    {
        ItemId = "price-yearly",
        TenantId = TenantId,
        PlanId = "plan-2",
        CurrencyCode = "CHF",
        UnitAmountMinor = 1_140_000,
        Interval = BillingInterval.Year,
        IntervalCount = 1,
        BillingAlignment = BillingAlignment.CalendarMonth,
        CalendarStubBasePriceId = "price-monthly",
        CalendarStubBaseUnitAmountMinor = MonthlyMinor,
        CalendarAnnualChargeTiming = timing,
        AutomaticDiscountBasisPoints = 800,
        Status = CatalogueStatus.Active
    };

    /// <summary>A 26-day card-free trial, so a 25 August signup converts on 20 September.</summary>
    private Plan TrialPlan() => TrialPlanOf(_trialDays);

    private static Plan TrialPlanOf(int trialDays) => new()
    {
        ItemId = "plan-2",
        TenantId = TenantId,
        Code = "tier-2",
        DisplayName = "Tier 2",
        Status = CatalogueStatus.Active,
        Version = 1,
        TrialDays = trialDays,
        TrialRequiresPaymentMethod = false,
        QuantityItems =
        [
            new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }
        ],
        Meters =
        [
            new PlanMeter { MeterKey = "screening", UnitLabel = "screening", IncludedQuantity = 500 }
        ]
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
