using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A free-opening-period campaign's temporary cap on one entitlement -- in force only while the
/// campaign's own opening period runs, and evaluated live rather than by anything that has to run
/// at the boundary.
/// </summary>
public sealed class EntitlementServiceCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _subscription;

    public EntitlementServiceCampaignTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
    }

    [Fact]
    public async Task Within_the_opening_period_the_overridden_entitlement_reads_the_campaigns_limit()
    {
        _subscription = NewSubscription(campaign: true);

        var result = await Service().GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        var seats = result.Value!.Entitlements.Single(entitlement => entitlement.Key == "seats");
        seats.Limit.Should().Be(1);
    }

    [Fact]
    public async Task After_the_opening_period_ends_the_plans_own_limit_applies_with_nothing_run_at_the_boundary()
    {
        _subscription = NewSubscription(campaign: true);
        // One second past CurrentPeriodEndUtc -- no worker, no scheduled job, just the clock this
        // call reads live.
        _time = new ControlledTimeProvider(
            new DateTimeOffset(_subscription.CurrentPeriodEndUtc.AddSeconds(1)));

        var result = await Service().GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        var seats = result.Value!.Entitlements.Single(entitlement => entitlement.Key == "seats");
        seats.Limit.Should().Be(5);
    }

    [Fact]
    public async Task The_override_never_touches_an_entitlement_it_does_not_name()
    {
        _subscription = NewSubscription(campaign: true);

        var result = await Service().GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        var screening = result.Value!.Entitlements.Single(entitlement => entitlement.Key == "pep_screening");
        screening.Limit.Should().Be(500);
    }

    [Fact]
    public async Task A_standard_discount_never_overrides_any_entitlement()
    {
        _subscription = NewSubscription(campaign: false);

        var result = await Service().GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        var seats = result.Value!.Entitlements.Single(entitlement => entitlement.Key == "seats");
        seats.Limit.Should().Be(5);
    }

    [Fact]
    public async Task A_first_annual_period_campaign_never_overrides_an_entitlement_either()
    {
        // Only FreeOpeningCalendarPeriod carries an entitlement override in this system -- a
        // FirstAnnualPeriod campaign that happened to snapshot one (it should not, by validation)
        // must still not be honoured here.
        _subscription = NewSubscription(campaign: true);
        _subscription.Discount!.Campaign.Kind = CampaignKind.FirstAnnualPeriod;

        var result = await Service().GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        var seats = result.Value!.Entitlements.Single(entitlement => entitlement.Key == "seats");
        seats.Limit.Should().Be(5);
    }

    private EntitlementService Service() => new(
        _subscriptions.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        new EntitlementSnapshotCache(new OptionsStub(), _time),
        _time);

    private static SubscriptionDetail NewSubscription(bool campaign) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot
        {
            Code = "team",
            Entitlements =
            [
                new PlanEntitlement
                {
                    Key = "seats", LimitKind = EntitlementLimitKind.Count, Limit = 5
                },
                new PlanEntitlement
                {
                    Key = "pep_screening", LimitKind = EntitlementLimitKind.Count, Limit = 500,
                    MeterKey = "screening"
                }
            ]
        },
        Discount = campaign
            ? new DiscountTerms
            {
                Code = "free1",
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    EntitlementOverride = new CampaignEntitlementOverride
                    {
                        EntitlementKey = "seats", Limit = 1
                    }
                }
            }
            : new DiscountTerms { Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2500 }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
