using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Whose allowance a caller's action draws on.
/// </summary>
/// <remarks>
/// Guards spending the wrong plan's allowance. Entitlement asking "what may this person do" and
/// usage asking "whose allowance does this consume" have to answer identically — if they disagree,
/// somebody is told they may act and the act is then counted against a plan they are not on.
/// <para>
/// The choice is made per meter rather than per caller, because the two kinds of plan cover
/// different things. Somebody with an allowance of their own still records against the
/// organization's plan for a meter their own says nothing about.
/// </para>
/// </remarks>
public sealed class SubscriberSubscriptionResolverTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionAssignmentRepository> _assignments = new();

    public SubscriberSubscriptionResolverTests()
    {
        _assignments
            .Setup(repository => repository.ListSubscriptionIdsForUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _subscriptions
            .Setup(repository => repository.ListLiveByIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task A_seat_a_caller_holds_comes_before_their_organizations_plan()
    {
        GivenSeat("sub-own", "ai_tokens");
        GivenOrganization("sub-org", "widgets");

        var resolved = await Resolver().ResolveAsync(Context(), Now, CancellationToken.None);

        resolved.Select(subscription => subscription.ItemId)
            .Should().BeEquivalentTo(["sub-own", "sub-org"], options => options.WithStrictOrdering(),
                because: "where both declare the same thing the more specific purchase answers, " +
                         "and the order is what carries that");
    }

    [Fact]
    public async Task Usage_of_a_meter_only_the_organization_sells_still_finds_it()
    {
        GivenSeat("sub-own", "ai_tokens");
        GivenOrganization("sub-org", "widgets");

        var resolved = await Resolver().ResolveAsync(Context(), Now, CancellationToken.None);

        SubscriberSubscriptionSelection.ForMeter(resolved, "widgets")!.ItemId
            .Should().Be("sub-org",
                because: "being given an allowance of one's own must not silently stop recording " +
                         "usage the organization is still paying for");
    }

    [Fact]
    public async Task Usage_of_a_meter_the_caller_holds_a_seat_for_spends_their_own()
    {
        GivenSeat("sub-own", "ai_tokens");
        GivenOrganization("sub-org", "ai_tokens");

        var resolved = await Resolver().ResolveAsync(Context(), Now, CancellationToken.None);

        SubscriberSubscriptionSelection.ForMeter(resolved, "ai_tokens")!.ItemId
            .Should().Be("sub-own",
                because: "both meter it, and spending the organization's shared pool when the " +
                         "person has their own is what the seat was bought to avoid");
    }

    [Fact]
    public async Task A_meter_nobody_sells_resolves_to_nothing()
    {
        GivenSeat("sub-own", "ai_tokens");
        GivenOrganization("sub-org", "widgets");

        var resolved = await Resolver().ResolveAsync(Context(), Now, CancellationToken.None);

        SubscriberSubscriptionSelection.ForMeter(resolved, "unsold").Should().BeNull();
    }

    [Fact]
    public async Task A_caller_with_no_seat_resolves_exactly_what_they_do_today()
    {
        GivenOrganization("sub-org", "widgets");

        var resolved = await Resolver().ResolveAsync(Context(), Now, CancellationToken.None);

        resolved.Should().ContainSingle().Which.ItemId.Should().Be("sub-org",
            because: "this is every subscriber in production, and the change has to be invisible " +
                     "to all of them");
    }

    [Fact]
    public async Task A_caller_with_no_user_is_never_asked_about_seats()
    {
        GivenOrganization("sub-org", "widgets");

        await Resolver().ResolveAsync(
            new SubscriptionContext(TenantId, OrganizationId, "actor-1", null),
            Now,
            CancellationToken.None);

        _assignments.Verify(
            repository => repository.ListSubscriptionIdsForUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "background work and machine tokens hold no seats, and asking is a round trip per " +
            "call for an answer that is always empty");
    }

    private static DateTime Now => new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionContext Context() =>
        new(TenantId, OrganizationId, "actor-1", "user-a");

    private void GivenSeat(string subscriptionId, string meterKey)
    {
        _assignments
            .Setup(repository => repository.ListSubscriptionIdsForUserAsync(
                TenantId, OrganizationId, "user-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscriptionId]);

        _subscriptions
            .Setup(repository => repository.ListLiveByIdsAsync(
                TenantId,
                It.Is<IReadOnlyCollection<string>>(ids => ids.Contains(subscriptionId)),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Metering(subscriptionId, meterKey, SubscriberScope.User)]);
    }

    private void GivenOrganization(string subscriptionId, string meterKey) =>
        _subscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Metering(subscriptionId, meterKey, SubscriberScope.Organization));

    private SubscriberSubscriptionResolver Resolver() =>
        new(_subscriptions.Object, _assignments.Object);

    private static SubscriptionDetail Metering(
        string subscriptionId,
        string meterKey,
        SubscriberScope scope) => new()
        {
            ItemId = subscriptionId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot
            {
                Code = "plan",
                SubscriberScope = scope,
                Meters = [new PlanMeter { MeterKey = meterKey, UnitLabel = "unit" }]
            }
        };
}
