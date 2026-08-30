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
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A free-opening-period campaign locks a plan change until its own opening period ends -- and
/// only the real change, never a preview.
/// </summary>
public sealed class SubscriptionPlanChangeServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionCatalogueRepository> _catalogue = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<ISubscriptionBillingProfileGuard> _billingProfile = new();
    private ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription();

    public SubscriptionPlanChangeServiceCampaignTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _catalogue
            .Setup(repository => repository.FindPlanByCodeAsync(
                TenantId, OrganizationId, "premium", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Plan
            {
                ItemId = "plan-2", TenantId = TenantId, Code = "premium",
                DisplayName = "Premium", Status = CatalogueStatus.Active
            });
        _catalogue
            .Setup(repository => repository.GetPriceAsync(
                TenantId, "price-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Price
            {
                ItemId = "price-2", TenantId = TenantId, PlanId = "plan-2", CurrencyCode = "CHF",
                UnitAmountMinor = 2_000, Interval = BillingInterval.Month, IntervalCount = 1,
                Status = CatalogueStatus.Active
            });

        _billingProfile
            .Setup(guard => guard.MissingFieldsAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task A_plan_change_is_refused_while_the_free_opening_period_is_still_running()
    {
        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_promotion_change_locked");
    }

    [Fact]
    public async Task A_plan_change_is_allowed_once_the_free_opening_period_has_ended()
    {
        _time = new ControlledTimeProvider(
            new DateTimeOffset(_subscription.CurrentPeriodEndUtc.AddSeconds(1)));

        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(), It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(), It.IsAny<PendingUsagePeriod>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>(), It.IsAny<SubscriptionDocumentSource?>()))
            .ReturnsAsync(true);
        _subscriptions
            .Setup(repository => repository.TryReserveSettlementAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<SettlementReservation>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ItemId = "acct-1", ProviderName = "STRIPE", DefaultPaymentMethodId = "pm-1",
                ProviderCustomerId = "cus_123"
            });
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("in_1", "corr-1"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_plan_change_preview_is_never_locked_even_during_the_free_opening_period()
    {
        var result = await Service().PreviewPlanChangeAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_standard_discount_never_locks_a_plan_change()
    {
        _subscription.Discount = new DiscountTerms
        {
            Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2_500
        };
        _subscriptions
            .Setup(repository => repository.TryChangePlanAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<PlanSnapshot>(),
                It.IsAny<PriceSnapshot>(), It.IsAny<List<SubscriptionQuantityItem>>(),
                It.IsAny<SubscriptionPlanSchedule>(), It.IsAny<PendingUsagePeriod>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<SubscriptionOutboxEvent>(),
                It.IsAny<CancellationToken>(), It.IsAny<SubscriptionDocumentSource?>()))
            .ReturnsAsync(true);
        _subscriptions
            .Setup(repository => repository.TryReserveSettlementAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<SettlementReservation>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ItemId = "acct-1", ProviderName = "STRIPE", DefaultPaymentMethodId = "pm-1",
                ProviderCustomerId = "cus_123"
            });
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("in_1", "corr-1"));

        var result = await Service().ChangePlanAsync(
            "sub-1", Request(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
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
        _time,
        billingProfile: _billingProfile.Object);

    private static ChangeSubscriptionPlanRequest Request() => new()
    {
        PlanCode = "premium",
        PriceId = "price-2"
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Version = 3,
        Plan = new PlanSnapshot { Code = "basic", DisplayName = "Basic" },
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF", UnitAmountMinor = 0,
            Interval = BillingInterval.Month, IntervalCount = 1
        },
        QuantityItems = [],
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Discount = new DiscountTerms
        {
            Code = "free1",
            Campaign = new CampaignTerms { Kind = CampaignKind.FreeOpeningCalendarPeriod }
        }
    };
}
