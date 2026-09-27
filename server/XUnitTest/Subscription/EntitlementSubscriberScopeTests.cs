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
/// What a subscriber may do when their own plan and their organization's are both live.
/// </summary>
/// <remarks>
/// Guards two ways of taking access away from people who are paying for it. The first is revoking
/// everything shared the moment someone is given a plan of their own: an organization-wise plan
/// covers what the organization shares -- widgets, channels -- and a user-wise plan covers one
/// person's own allowance, so resolving only the subscriber's would silently cancel the rest.
/// The second is answering one person's allowance with another's, which a cache that could not
/// tell two subscribers in one organization apart would do on every hit.
/// </remarks>
public sealed class EntitlementSubscriberScopeTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string OrganizationKey = "widgets";
    private const string UserKey = "ai_credits";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriberSubscriptionResolver> _resolver = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private readonly IEntitlementSnapshotCache _cache;

    private string _actingUserId = "user-a";

    public EntitlementSubscriberScopeTests()
    {
        _cache = new EntitlementSnapshotCache(new OptionsStub(), _time);

        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", _actingUserId)));

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SubscriptionUsageCounter { Balance = 0 });
    }

    [Fact]
    public async Task A_subscriber_with_their_own_plan_still_reaches_what_the_organization_shares()
    {
        GivenLive(OrganizationSubscription(), UserSubscription());

        var snapshot = await Service().GetAsync(
            fresh: false, null, "corr-1", CancellationToken.None);

        snapshot.Value!.Entitlements.Select(entitlement => entitlement.Key)
            .Should().Contain(OrganizationKey,
                because: "the two plans cover different things, so being given an allowance of " +
                         "one's own must not cancel the organization's widgets and channels");
    }

    [Fact]
    public async Task A_subscriber_reaches_their_own_allowance_as_well()
    {
        GivenLive(OrganizationSubscription(), UserSubscription());

        var snapshot = await Service().GetAsync(
            fresh: false, null, "corr-1", CancellationToken.None);

        snapshot.Value!.Entitlements.Select(entitlement => entitlement.Key)
            .Should().Contain(UserKey,
                because: "the per-person allowance is the whole reason a user-wise plan is sold");
    }

    [Fact]
    public async Task Where_both_plans_declare_a_key_the_subscribers_own_answers_it()
    {
        var user = UserSubscription();
        user.Plan.Entitlements.Add(new PlanEntitlement
        {
            Key = OrganizationKey,
            LimitKind = EntitlementLimitKind.Count,
            Limit = 999
        });

        GivenLive(OrganizationSubscription(), user);

        var snapshot = await Service().GetAsync(
            fresh: false, null, "corr-1", CancellationToken.None);

        snapshot.Value!.Entitlements
            .Single(entitlement => entitlement.Key == OrganizationKey)
            .Limit.Should().Be(999,
                because: "a plan bought for one person is the more specific answer, the same way " +
                         "an organization's own catalogue entry already wins over the tenant's");
    }

    [Fact]
    public async Task A_subscriber_with_no_plan_of_their_own_sees_exactly_what_they_saw_before()
    {
        GivenLive(OrganizationSubscription());

        var snapshot = await Service().GetAsync(
            fresh: false, null, "corr-1", CancellationToken.None);

        snapshot.Value!.PlanCode.Should().Be("organization-plan",
            because: "the overwhelming majority of subscribers have no user-wise plan, and this " +
                     "change must be invisible to every one of them");
    }

    [Fact]
    public async Task One_subscribers_allowance_is_never_served_to_another()
    {
        GivenSeatsFor("user-a", OrganizationSubscription(), UserSubscription());
        GivenSeatsFor("user-b", OrganizationSubscription());

        var service = Service();

        _actingUserId = "user-a";
        await service.GetAsync(fresh: false, null, "corr-1", CancellationToken.None);

        _actingUserId = "user-b";
        var second = await service.GetAsync(
            fresh: false, null, "corr-2", CancellationToken.None);

        second.Value!.Entitlements.Select(entitlement => entitlement.Key)
            .Should().NotContain(UserKey,
                because: "a cache keyed on the organization alone would hand the second person " +
                         "the first person's plan, and they would spend an allowance they never " +
                         "bought");
    }

    [Fact]
    public void Invalidating_an_organization_clears_every_subscriber_it_cached()
    {
        _cache.Invalidate(TenantId, OrganizationId);

        // Nothing to assert beyond the sweep being organization-wide, which the read below proves:
        // a per-subscriber removal would have left user-a's entry behind.
        var loads = 0;

        Task<IReadOnlyList<SubscriptionDetail>> Load()
        {
            loads++;

            return Task.FromResult<IReadOnlyList<SubscriptionDetail>>([OrganizationSubscription()]);
        }

        _cache.GetAsync(TenantId, OrganizationId, "user-a", Load).GetAwaiter().GetResult();
        _cache.Invalidate(TenantId, OrganizationId);
        _cache.GetAsync(TenantId, OrganizationId, "user-a", Load).GetAwaiter().GetResult();

        loads.Should().Be(2,
            because: "every subscriber's entry carries the organization's own subscription, so a " +
                     "change to it has to drop all of them — the processor reporting the change " +
                     "holds one subscription and cannot enumerate who cached it");
    }

    /// <summary>
    /// Puts the organization's own subscription in place, and gives the acting user these seats.
    /// </summary>
    private void GivenLive(SubscriptionDetail organization, params SubscriptionDetail[] seats) =>
        GivenSeatsFor(_actingUserId, organization, seats);

    private void GivenSeatsFor(
        string userId,
        SubscriptionDetail? organization,
        params SubscriptionDetail[] seats)
    {
        var resolved = new List<ResolvedSubscription>(
            seats.Select((seat, index) => new ResolvedSubscription(seat, index + 1)));

        if (organization is not null)
        {
            resolved.Add(new ResolvedSubscription(organization, SeatNumber: null));
        }

        _resolver
            .Setup(resolver => resolver.ResolveAsync(
                It.Is<SubscriptionContext>(context => context.UserId == userId),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
    }

    private EntitlementService Service() => new(
        _subscriptions.Object,
        _resolver.Object,
        _usage.Object,
        new MeterAllowanceResolver(_usage.Object),
        _contextResolver.Object,
        _cache,
        _time);

    private static SubscriptionDetail OrganizationSubscription() =>
        NewSubscription("sub-org", string.Empty, "organization-plan", OrganizationKey);

    private static SubscriptionDetail UserSubscription() =>
        NewSubscription("sub-user", "user-a", "starter", UserKey);

    private static SubscriptionDetail NewSubscription(
        string itemId,
        string subscriberUserId,
        string planCode,
        string entitlementKey) => new()
        {
            ItemId = itemId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            CurrentPeriodEndUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
            Plan = new PlanSnapshot
            {
                Code = planCode,
                Entitlements =
                [
                    new PlanEntitlement
                    {
                        Key = entitlementKey,
                        LimitKind = EntitlementLimitKind.Unlimited
                    }
                ]
            }
        };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
