using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Releasing a campaign when the subscription it was reserved for never activated, and never
/// releasing one that did.
/// </summary>
public sealed class SubscriptionCancellationServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _subscription;

    public SubscriptionCancellationServiceCampaignTests()
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

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Cancelling_an_incomplete_campaign_subscription_releases_it()
    {
        _subscription = NewSubscription(SubscriptionStatus.Incomplete, campaign: true);

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                TenantId, "discount-1", "sub-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancelling_an_active_campaign_subscription_never_releases_it()
    {
        // The subscription already activated -- its campaign is permanently redeemed, and a later
        // cancellation, immediate or not, must not give the slot back to a different organization.
        _subscription = NewSubscription(SubscriptionStatus.Active, campaign: true);
        _subscription.PendingAnnualPeriod = null;

        var result = await Service().CancelAsync(
            "sub-1", immediately: true, null, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cancelling_an_incomplete_standard_discount_subscription_never_touches_the_ledger()
    {
        _subscription = NewSubscription(SubscriptionStatus.Incomplete, campaign: false);

        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_cancellation_that_scheduled_for_period_end_rather_than_ending_now_never_releases()
    {
        // Scheduling a future cancellation on an Active subscription does not go through
        // EndNowAsync at all -- confirmed here so nobody assumes a release runs on that path too.
        _subscription = NewSubscription(SubscriptionStatus.Active, campaign: true);
        _subscription.PendingAnnualPeriod = null;

        await Service().CancelAsync(
            "sub-1", immediately: false, null, null, "corr-1", CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private SubscriptionCancellationService Service() => new(
        _subscriptions.Object,
        _links.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _cache.Object,
        NullLogger<SubscriptionCancellationService>.Instance,
        _time,
        redemptions: _redemptions.Object);

    private static SubscriptionDetail NewSubscription(SubscriptionStatus status, bool campaign) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = status,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        NextFeeBillingAtUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8900 },
        Discount = campaign
            ? new DiscountTerms
            {
                Code = "free1",
                DiscountId = "discount-1",
                DiscountVersion = 1,
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    OneUsePerOrganization = true
                }
            }
            : new DiscountTerms { Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2500 }
    };
}
