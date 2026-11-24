using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Spec 008 (#808): paying for several periods upfront.
/// </summary>
public sealed class PrepaidPeriodsTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private static readonly BillingSchedule MonthlyUtc = new()
    {
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        TimeZoneId = "UTC",
        AnchorDayOfMonth = 1
    };

    // ---- Opening charge ---------------------------------------------------------------------

    [Fact]
    public void The_extra_periods_take_the_code_only_as_long_as_it_lasts()
    {
        var subscription = Active(unitAmountMinor: 10_000);
        subscription.PrepaidPeriods = 5;
        subscription.Price.TaxRateBasisPoints = 800;
        subscription.Price.TaxMode = TaxMode.Exclusive;
        subscription.Discount = new DiscountTerms
        {
            Code = "HALF",
            Kind = DiscountKind.Percent,
            PercentBasisPoints = 5_000,
            DurationPeriods = 2
        };

        // The opening period already took the code once, so one of the four extra periods may.
        var extra = SubscriptionAmountCalculator.PrepaidExtraCharge(
            subscription, openingProrated: false, annual: null, openingDiscounted: true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        extra.Periods.Should().Be(4);
        extra.DiscountedPeriods.Should().Be(2);
        extra.Charge.AmountMinor.Should().Be(5_400 + 3 * 10_800);
        extra.Charge.GrossAmountMinor.Should().Be(4 * 10_000);
        extra.Charge.PromotionalDiscountMinor.Should().Be(5_000);
        extra.Charge.TaxAmountMinor.Should().Be(400 + 3 * 800);
    }

    [Fact]
    public void After_a_calendar_month_stub_every_prepaid_month_is_extra()
    {
        var subscription = Active(unitAmountMinor: 10_000);
        subscription.PrepaidPeriods = 3;

        var extra = SubscriptionAmountCalculator.PrepaidExtraCharge(
            subscription, openingProrated: true, annual: null, openingDiscounted: false,
            new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc));

        extra.Periods.Should().Be(3);
        extra.Charge.AmountMinor.Should().Be(3 * 10_000);
    }

    // ---- Signup ------------------------------------------------------------------------------

    [Fact]
    public async Task A_signup_for_three_months_freezes_three_months_and_the_term_end()
    {
        var signup = new SignupHarness { MaxPrepaidPeriods = 5 };

        var result = await signup.CreateAsync(prepaidPeriods: 3);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var created = signup.Created!;
        created.InitialChargeAmountMinor.Should().Be(3 * 12 * 8_900);
        created.PrepaidPeriods.Should().Be(3);
        created.PrepaidThroughUtc!.Value.Date.Should().Be(new DateTime(2026, 11, 14));
        created.NextFeeBillingAtUtc.Should().Be(created.CurrentPeriodEndUtc,
            "the schedule still turns over monthly; those renewals simply charge nothing");
    }

    [Fact]
    public async Task A_calendar_month_signup_pays_the_days_to_the_first_plus_three_whole_months()
    {
        void Calendar(Price price) => price.BillingAlignment = BillingAlignment.CalendarMonth;
        var single = new SignupHarness { MaxPrepaidPeriods = 5, PriceSetup = Calendar };
        var prepaid = new SignupHarness { MaxPrepaidPeriods = 5, PriceSetup = Calendar };

        await single.CreateAsync(prepaidPeriods: null);
        var result = await prepaid.CreateAsync(prepaidPeriods: 3);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        prepaid.Created!.InitialChargeProrated.Should().BeTrue("14 to 31 August is still charged by the day");
        (prepaid.Created.InitialChargeAmountMinor - single.Created!.InitialChargeAmountMinor)
            .Should().Be(3 * 12 * 8_900, "September, October and November are whole months");
        LocalDate(prepaid.Created.PrepaidThroughUtc!.Value).Should().Be(new DateTime(2026, 12, 1));
    }

    [Fact]
    public async Task A_calendar_year_signup_pays_the_stub_plus_five_whole_years_at_checkout()
    {
        void CalendarYear(Price price)
        {
            price.Interval = BillingInterval.Year;
            price.UnitAmountMinor = 89_000;
            price.BillingAlignment = BillingAlignment.CalendarMonth;
            price.CalendarStubBasePriceId = "price-monthly";
            price.CalendarStubBaseUnitAmountMinor = 8_900;
        }

        var single = new SignupHarness { MaxPrepaidPeriods = 5, PriceSetup = CalendarYear };
        var prepaid = new SignupHarness { MaxPrepaidPeriods = 5, PriceSetup = CalendarYear };

        await single.CreateAsync(prepaidPeriods: null);
        var result = await prepaid.CreateAsync(prepaidPeriods: 5);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var created = prepaid.Created!;
        created.PendingAnnualPeriod!.CollectedWithCheckout.Should().BeTrue(
            "a prepaid term collects its first year at checkout too");
        LocalDate(created.PendingAnnualPeriod.StartUtc).Should().Be(new DateTime(2026, 9, 1));
        // The single signup collects only the stub (its year waits for the boundary).
        (created.InitialChargeAmountMinor - single.Created!.InitialChargeAmountMinor)
            .Should().Be(5 * 12 * 89_000);
        LocalDate(created.PrepaidThroughUtc!.Value).Should().Be(new DateTime(2031, 9, 1));
        created.PrepaidDiscountPeriods.Should().Be(0);
    }

    [Fact]
    public async Task One_period_is_an_ordinary_signup()
    {
        var signup = new SignupHarness { MaxPrepaidPeriods = 5 };

        await signup.CreateAsync(prepaidPeriods: null);

        signup.Created!.InitialChargeAmountMinor.Should().Be(12 * 8_900);
        signup.Created.PrepaidThroughUtc.Should().BeNull();
        signup.Created.PrepaidPeriods.Should().Be(0);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(5, 6)]
    public async Task More_periods_than_the_price_allows_are_refused(int? max, int requested)
    {
        var signup = new SignupHarness { MaxPrepaidPeriods = max };

        var result = await signup.CreateAsync(prepaidPeriods: requested);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_prepaid_periods_unavailable");
        signup.Created.Should().BeNull();
    }

    [Fact]
    public async Task A_signup_with_a_trial_cannot_prepay()
    {
        var signup = new SignupHarness { MaxPrepaidPeriods = 5 };
        signup.Plan.TrialDays = 14;

        var result = await signup.CreateAsync(prepaidPeriods: 2);

        result.ErrorCode.Should().Be("subscription_prepaid_periods_unavailable");
    }

    [Fact]
    public void Zero_periods_fails_validation()
    {
        var result = new CreateSubscriptionRequestValidator().Validate(new CreateSubscriptionRequest
        {
            PlanCode = "professional",
            PriceId = "price-1",
            TimeZoneId = "UTC",
            PrepaidPeriods = 0
        });

        result.Errors.Should().Contain(error => error.ErrorCode == "subscription_prepaid_periods_invalid");
    }

    // ---- Renewal -----------------------------------------------------------------------------

    [Fact]
    public async Task A_renewal_inside_the_term_charges_nothing_and_needs_no_card()
    {
        var renewal = new RenewalHarness();
        renewal.Account.DefaultPaymentMethodId = null;
        var subscription = Active(unitAmountMinor: 8_900);
        subscription.CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.PrepaidThroughUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        await renewal.Service().RenewAsync(subscription, CancellationToken.None);

        renewal.Gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        renewal.Transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        renewal.Transition.CurrentPeriodEndUtc.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        renewal.Transition.DiscountPeriodsApplied.Should().Be(0);
    }

    [Fact]
    public async Task The_renewal_at_the_term_end_charges_as_usual()
    {
        var renewal = new RenewalHarness();
        var subscription = Active(unitAmountMinor: 8_900);
        subscription.CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.PrepaidThroughUtc = subscription.CurrentPeriodEndUtc;

        await renewal.Service().RenewAsync(subscription, CancellationToken.None);

        renewal.Charge!.AmountMinor.Should().Be(8_900);
    }

    // ---- Paid-through date, cancellation, plan changes ----------------------------------------

    [Fact]
    public void The_paid_through_date_is_the_term_end_while_the_term_runs()
    {
        var subscription = InsideTerm();

        SubscriptionPaidPeriod.PaidThroughUtc(subscription).Should().Be(subscription.PrepaidThroughUtc);
    }

    [Fact]
    public void An_unpaid_checkout_has_no_prepaid_term()
    {
        var subscription = InsideTerm();
        subscription.Status = SubscriptionStatus.Incomplete;

        SubscriptionPaidPeriod.PrepaidTermEndUtc(subscription).Should().BeNull();
        SubscriptionPaidPeriod.PaidThroughUtc(subscription).Should().Be(subscription.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task Cancel_now_inside_the_term_runs_to_the_term_end_and_cannot_be_escalated()
    {
        var cancellation = new CancellationHarness(InsideTerm());

        var result = await cancellation.Service().CancelAsync(
            "sub-1", immediately: true, "too expensive", null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        cancellation.Transition!.NewStatus.Should().Be(SubscriptionStatus.Active);
        cancellation.Transition.CancelAtPeriodEnd.Should().BeTrue();
        cancellation.Transition.CanCancelImmediately.Should().BeFalse();
        cancellation.Transition.CurrentPeriodEndUtc.Should().Be(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void A_cadence_change_inside_the_term_waits_for_the_term_end_even_when_it_costs_more()
    {
        var target = new PriceSnapshot { Interval = BillingInterval.Year, IntervalCount = 1, UnitAmountMinor = 99_000 };

        PlanChangeClassifier.Classify(InsideTerm(), target, settlementMinor: 50_000)
            .Should().Be(PlanChangeTiming.NextRenewal);
    }

    [Fact]
    public void An_increase_inside_the_term_also_pays_the_difference_for_every_prepaid_period_to_come()
    {
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var target = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 20_000 };
        var withTerm = InsideTerm();
        var withoutTerm = InsideTerm();
        withoutTerm.PrepaidThroughUtc = null;

        var prepaid = Prorate(withTerm, target, now);
        var ordinary = Prorate(withoutTerm, target, now);

        (prepaid.ChargeMinor - ordinary.ChargeMinor).Should().Be(3 * (20_000 - 10_000),
            "September, October and November were bought at the old price");
    }

    [Fact]
    public void The_response_reports_the_term_and_its_end_as_the_next_payment()
    {
        var subscription = InsideTerm();
        subscription.PrepaidPeriods = 4;

        var response = new SubscriptionResponseMapper().ToResponse(subscription);

        response.PrepaidPeriods.Should().Be(4);
        response.PrepaidThroughUtc.Should().Be(subscription.PrepaidThroughUtc);
        response.NextPaymentAtUtc.Should().Be(subscription.PrepaidThroughUtc);
    }

    [Fact]
    public void An_increase_during_a_calendar_stub_values_the_months_to_come_whole()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var target = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            UnitAmountMinor = 20_000,
            BillingAlignment = BillingAlignment.CalendarMonth
        };
        var withTerm = InsideTerm();
        withTerm.Price.BillingAlignment = BillingAlignment.CalendarMonth;
        withTerm.CurrentPeriodStartUtc = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        var withoutTerm = InsideTerm();
        withoutTerm.Price.BillingAlignment = BillingAlignment.CalendarMonth;
        withoutTerm.CurrentPeriodStartUtc = withTerm.CurrentPeriodStartUtc;
        withoutTerm.PrepaidThroughUtc = null;

        var difference = Prorate(withTerm, target, now).ChargeMinor - Prorate(withoutTerm, target, now).ChargeMinor;

        difference.Should().Be(3 * (20_000 - 10_000), "the stub's fraction must not shrink the whole months");
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static DateTime LocalDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich")).Date;

    private static ProrationOutcome Prorate(SubscriptionDetail subscription, PriceSnapshot target, DateTime now) =>
        SubscriptionProrationCalculator.Calculate(
            subscription,
            subscription.Plan,
            target,
            subscription.QuantityItems,
            now,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            targetIsCurrentPeriod: true);

    /// <summary>August of a term bought through November: three prepaid months still to come.</summary>
    private static SubscriptionDetail InsideTerm()
    {
        var subscription = Active(unitAmountMinor: 10_000);
        subscription.CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.NextFeeBillingAtUtc = subscription.CurrentPeriodEndUtc;
        subscription.PrepaidPeriods = 4;
        subscription.PrepaidThroughUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        return subscription;
    }

    private static SubscriptionDetail Active(long unitAmountMinor) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = unitAmountMinor },
        FeeSchedule = MonthlyUtc
    };

    private sealed class SignupHarness
    {
        private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
        private readonly Mock<ISubscriptionRepository> _subscriptions = new();
        private readonly Mock<IBillingAccountRepository> _accounts = new();
        private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();

        public Plan Plan { get; } = new()
        {
            ItemId = "plan-1",
            TenantId = TenantId,
            Code = "professional",
            DisplayName = "Professional",
            Status = CatalogueStatus.Active,
            QuantityItems = [new PlanQuantityItem { ItemKey = "seat", UnitLabel = "seat", DefaultQuantity = 1 }]
        };

        public int? MaxPrepaidPeriods { get; init; }

        public Action<Price>? PriceSetup { get; init; }

        public SubscriptionDetail? Created { get; private set; }

        public SignupHarness()
        {
            _billingProfile
                .Setup(guard => guard.MissingFieldsAsync(TenantId, OrganizationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _billingProfile
                .Setup(guard => guard.ContactDefaultsAsync(TenantId, OrganizationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BillingContactDefaults("Ada Byron", "billing@northwind.example"));
            _catalogue
                .Setup(repository => repository.FindPlanByCodeAsync(
                    TenantId, OrganizationId, "professional", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Plan);
            _catalogue
                .Setup(repository => repository.GetPriceAsync(TenantId, "price-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var price = new Price
                    {
                        ItemId = "price-1",
                        TenantId = TenantId,
                        PlanId = "plan-1",
                        CurrencyCode = "CHF",
                        UnitAmountMinor = 8_900,
                        Interval = BillingInterval.Month,
                        IntervalCount = 1,
                        QuantityItemKey = "seat",
                        MaxPrepaidPeriods = MaxPrepaidPeriods,
                        Status = CatalogueStatus.Active
                    };
                    PriceSetup?.Invoke(price);
                    return price;
                });
            _accounts
                .Setup(repository => repository.GetOrCreateAndReconcileAsync(
                    It.IsAny<BillingAccount>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((BillingAccount account, CancellationToken _) => account);
            _subscriptions
                .Setup(repository => repository.TryCreateAsync(It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()))
                .Callback<SubscriptionDetail, CancellationToken>((subscription, _) => Created = subscription)
                .ReturnsAsync(true);
            _subscriptions
                .Setup(repository => repository.GetLiveAsync(
                    TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SubscriptionDetail?)null);
            _subscriptions
                .Setup(repository => repository.GetIncompleteAsync(TenantId, OrganizationId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((SubscriptionDetail?)null);
        }

        public Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(int? prepaidPeriods) =>
            new SubscriptionCreationService(
                    _catalogue.Object,
                    _subscriptions.Object,
                    new Mock<ISubscriptionDiscountRepository>().Object,
                    _accounts.Object,
                    new CreateSubscriptionRequestValidator(),
                    NullLogger<SubscriptionCreationService>.Instance,
                    new ControlledTimeProvider(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero)),
                    billingProfile: _billingProfile.Object)
                .CreateAsync(
                    new CreateSubscriptionRequest
                    {
                        PlanCode = "professional",
                        PriceId = "price-1",
                        TimeZoneId = "Europe/Zurich",
                        Quantities = [new SubscriptionQuantityRequest { ItemKey = "seat", Quantity = 12 }],
                        PrepaidPeriods = prepaidPeriods
                    },
                    new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1"),
                    "corr-1",
                    CancellationToken.None);
    }

    private sealed class RenewalHarness
    {
        private readonly Mock<ISubscriptionRepository> _subscriptions = new();
        private readonly Mock<IBillingAccountRepository> _accounts = new();

        public Mock<ISubscriptionBillingGateway> Gateway { get; } = new();

        public BillingAccount Account { get; } = new()
        {
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = "pm-1"
        };

        public SubscriptionTransition? Transition { get; private set; }

        public SubscriptionChargeRequest? Charge { get; private set; }

        public RenewalHarness()
        {
            _accounts
                .Setup(repository => repository.GetAsync(TenantId, "acct-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Account);
            _subscriptions
                .Setup(repository => repository.TryTransitionAsync(
                    TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, SubscriptionTransition, CancellationToken>((_, _, transition, _) => Transition = transition)
                .ReturnsAsync(true);
            Gateway
                .Setup(gateway => gateway.ChargeAsync(
                    It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<SubscriptionChargeRequest, string, string, CancellationToken>((request, _, _, _) => Charge = request)
                .ReturnsAsync(SubscriptionOperationResult<string>.Success("pay-1", "corr-1"));
        }

        public SubscriptionRenewalService Service() => new(
            _subscriptions.Object,
            _accounts.Object,
            Gateway.Object,
            new SubscriptionOutboxEventFactory(),
            new Mock<IEntitlementSnapshotCache>().Object,
            new OptionsStub(),
            NullLogger<SubscriptionRenewalService>.Instance,
            new ControlledTimeProvider(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero)));
    }

    private sealed class CancellationHarness
    {
        private readonly Mock<ISubscriptionRepository> _subscriptions = new();
        private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();

        public SubscriptionTransition? Transition { get; private set; }

        public CancellationHarness(SubscriptionDetail subscription)
        {
            _contextResolver
                .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SubscriptionContextResolution.Resolved(
                    new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));
            _subscriptions
                .Setup(repository => repository.GetAsync(TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(subscription);
            _subscriptions
                .Setup(repository => repository.TryTransitionAsync(
                    TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, SubscriptionTransition, CancellationToken>((_, _, transition, _) => Transition = transition)
                .ReturnsAsync(true);
        }

        public SubscriptionCancellationService Service() => new(
            _subscriptions.Object,
            new Mock<ISubscriptionPaymentLinkRepository>().Object,
            _contextResolver.Object,
            new SubscriptionOutboxEventFactory(),
            new SubscriptionResponseMapper(),
            new Mock<IBillingAccountRepository>().Object,
            new Mock<IEntitlementSnapshotCache>().Object,
            NullLogger<SubscriptionCancellationService>.Instance,
            new ControlledTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
            new Mock<ISubscriptionWorkScheduler>().Object,
            new Mock<IUsagePeriodClosureRepository>().Object);
    }

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
