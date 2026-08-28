using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
/// A free-opening-period campaign locks a quantity change until its own opening period ends.
/// </summary>
public sealed class SubscriptionQuantityChangeServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription();

    public SubscriptionQuantityChangeServiceCampaignTests()
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
    }

    [Fact]
    public async Task A_quantity_change_is_refused_while_the_free_opening_period_is_still_running()
    {
        var result = await Service().ChangeAsync(
            "sub-1", Request(2), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_promotion_change_locked");
    }

    [Fact]
    public async Task A_quantity_change_preview_is_never_locked_even_during_the_free_opening_period()
    {
        var result = await Service().PreviewAsync(
            "sub-1", Request(2), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_standard_discount_never_locks_a_quantity_change()
    {
        // Deliberately not asserting the whole change succeeds -- that needs a much larger mock
        // setup unrelated to what this test is about. Whatever else this does or does not permit,
        // it must not be this lock, which only exists for a free-opening-period campaign.
        _subscription.Discount = new DiscountTerms
        {
            Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2_500
        };

        var result = await Service().ChangeAsync(
            "sub-1", Request(2), "corr-1", CancellationToken.None);

        result.ErrorCode.Should().NotBe("subscription_promotion_change_locked");
    }

    private SubscriptionQuantityChangeService Service() => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new ChangeQuantityRequestValidator(),
        NullLogger<SubscriptionQuantityChangeService>.Instance,
        _time);

    private static ChangeQuantityRequest Request(long quantity) => new()
    {
        Version = 7,
        Quantities = [new QuantityChangeItemRequest { ItemKey = "user", Quantity = quantity }]
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        QuantityItems =
        [
            new SubscriptionQuantityItem
            {
                ItemKey = "user", UnitLabel = "user", Quantity = 1, UnitAmountMinor = 14_500
            }
        ],
        Price = new PriceSnapshot
        {
            UnitAmountMinor = 14_500,
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            QuantityItemKey = "user"
        },
        Plan = new PlanSnapshot
        {
            Code = "team",
            QuantityItems = [new PlanQuantityItem { ItemKey = "user", UnitLabel = "user", MinQuantity = 1 }]
        },
        Discount = new DiscountTerms
        {
            Code = "free1",
            Campaign = new CampaignTerms { Kind = CampaignKind.FreeOpeningCalendarPeriod }
        }
    };
}
