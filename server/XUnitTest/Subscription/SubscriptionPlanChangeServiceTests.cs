using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>Moving a live subscription to a different price, mid-period, with proration.</summary>
public sealed class SubscriptionPlanChangeServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription(SubscriptionStatus.Active, 1_000);
    private BillingAccount? _account = new()
    {
        ItemId = "acct-1",
        ProviderName = "STRIPE",
        DefaultPaymentMethodId = "pm-1",
        ProviderCustomerId = "cus_123"
    };

    public SubscriptionPlanChangeServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId,
                "sub-1",
                It.IsAny<int>(),
                It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<long>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "premium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPlan());

        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(2_000));

        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _account);

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("in_1", "corr-1"));
    }

    [Fact]
    public async Task An_upgrade_charges_the_prorated_difference_through_the_gateway()
    {
        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.Is<SubscriptionChargeRequest>(request => request.AmountMinor == 1_000),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_declined_charge_leaves_the_subscription_unchanged()
    {
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(),
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_downgrade_never_calls_the_gateway()
    {
        _subscription = NewSubscription(SubscriptionStatus.Active, 2_000);
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(1_000));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_trial_swap_never_calls_the_gateway_or_touches_credit()
    {
        _subscription = NewSubscription(SubscriptionStatus.Trialing, 1_000);
        _subscription.CreditBalanceMinor = 0;

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _subscriptions.Verify(
            repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(),
                0L,
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_different_currency_is_refused()
    {
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPrice(2_000, currencyCode: "EUR"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_currency_mismatch");
    }

    [Fact]
    public async Task A_different_billing_interval_is_refused()
    {
        var price = NewPrice(2_000);
        price.Interval = BillingInterval.Year;
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(price);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_interval_mismatch");
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Unpaid)]
    [InlineData(SubscriptionStatus.Canceled)]
    public async Task An_ineligible_status_is_a_conflict(SubscriptionStatus status)
    {
        _subscription = NewSubscription(status, 1_000);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
    }

    [Fact]
    public async Task A_lost_compare_and_set_is_reported_as_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<PlanSnapshot>(), It.IsAny<PriceSnapshot>(),
                It.IsAny<List<SubscriptionQuantityItem>>(), It.IsAny<long>(),
                It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_conflict");
    }

    [Fact]
    public async Task No_payment_method_refuses_an_upgrade()
    {
        _account = new BillingAccount
        {
            ItemId = "acct-1",
            ProviderName = "STRIPE",
            DefaultPaymentMethodId = null
        };

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_plan_change_no_payment_method");
    }

    private SubscriptionPlanChangeService Service() => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _catalogue.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new SubscriptionResponseMapper(),
        new ChangeSubscriptionPlanRequestValidator(),
        NullLogger<SubscriptionPlanChangeService>.Instance,
        _time);

    private static ChangeSubscriptionPlanRequest Request() => new()
    {
        PlanCode = "premium",
        PriceId = "price-2"
    };

    private static SubscriptionDetail NewSubscription(
        SubscriptionStatus status, long currentAmountMinor) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CurrencyCode = "CHF",
        Version = 3,
        Plan = new PlanSnapshot { Code = "basic", DisplayName = "Basic" },
        Price = NewPriceSnapshot(currentAmountMinor),
        QuantityItems = [],
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static PriceSnapshot NewPriceSnapshot(long unitAmountMinor) => new()
    {
        CurrencyCode = "CHF",
        UnitAmountMinor = unitAmountMinor,
        Interval = BillingInterval.Month,
        IntervalCount = 1
    };

    private static Plan NewPlan() => new()
    {
        ItemId = "plan-2",
        TenantId = TenantId,
        Code = "premium",
        DisplayName = "Premium",
        Status = CatalogueStatus.Active
    };

    private static Price NewPrice(long unitAmountMinor, string currencyCode = "CHF") => new()
    {
        ItemId = "price-2",
        TenantId = TenantId,
        PlanId = "plan-2",
        CurrencyCode = currencyCode,
        UnitAmountMinor = unitAmountMinor,
        Interval = BillingInterval.Month,
        IntervalCount = 1,
        Status = CatalogueStatus.Active
    };
}
