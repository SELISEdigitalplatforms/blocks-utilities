using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Who may be put on a subscription, and how many of them.
/// </summary>
/// <remarks>
/// Guards giving away access nobody paid for. A subscription was charged for a number of people, so
/// putting more than that on it hands the plan out free; putting anyone on an organization's own
/// subscription would give one person what the whole organization shares; and putting somebody on a
/// subscription that no longer grants anything sells them nothing.
/// <para>
/// How many were paid for depends on how the plan charges, which is what most of these turn on: a
/// price that multiplies a quantity was bought per person, and a flat price was bought for up to a
/// ceiling.
/// </para>
/// </remarks>
public sealed class SubscriptionMemberServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionAssignmentRepository> _assignments = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = UserWise(seats: 3);
    private long _held;

    public SubscriptionMemberServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "admin-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _assignments
            .Setup(repository => repository.CountActiveAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _held);

        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        _assignments
            .Setup(repository => repository.TryReleaseAsync(
                TenantId, SubscriptionId, It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemberReleaseOutcome.Released);
    }

    [Fact]
    public async Task A_place_goes_to_the_person_the_request_names_not_the_caller()
    {
        var written = new List<SubscriptionAssignment>();

        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAssignment, CancellationToken>((a, _) => written.Add(a))
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        await Assign("user-b");

        written.Should().ContainSingle().Which.UserId.Should().Be("user-b",
            because: "an administrator fills places for other people, so taking the holder from " +
                     "the token would put the administrator on every one of them");
        written[0].AssignedByUserId.Should().Be("admin-1",
            because: "who handed out the place is what an audit asks about later");
    }

    [Fact]
    public async Task Everyone_a_subscription_was_bought_for_can_be_named_in_one_call()
    {
        _subscription = UserWise(seats: 10);

        var result = await Assign("u1", "u2", "u3", "u4", "u5", "u6", "u7", "u8", "u9", "u10");

        result.Value!.Assigned.Should().HaveCount(10,
            because: "an administrator filling a ten-person subscription should not make ten " +
                     "calls and reconcile ten answers");
        result.Value.Refused.Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_larger_than_the_subscription_fills_it_and_says_who_missed_out()
    {
        _subscription = UserWise(seats: 3);

        var result = await Assign("u1", "u2", "u3", "u4", "u5");

        result.IsSuccess.Should().BeTrue(
            because: "three assignments landed, and calling the whole thing a failure would send " +
                     "an administrator looking for people who are already on it");
        result.Value!.Assigned.Should().HaveCount(3);
        result.Value.Refused.Select(refusal => refusal.UserId).Should()
            .BeEquivalentTo(["u4", "u5"], options => options.WithStrictOrdering(),
                because: "the answer has to name who missed out, in the order they were given");
    }

    [Fact]
    public async Task A_name_repeated_in_one_call_takes_one_place()
    {
        _subscription = UserWise(seats: 3);

        var result = await Assign("u1", "u1", "u2");

        result.Value!.Assigned.Should().HaveCount(2,
            because: "a duplicate in a pasted list is a typo, not a second person, and spending " +
                     "two of three places on it would be charged for");
    }

    [Fact]
    public async Task Assigning_more_people_than_were_paid_for_is_refused()
    {
        _held = 3;

        (await Assign("user-d")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "places are what the subscription was charged for, so a fourth person " +
                         "on three paid places gives the plan away");
    }

    [Fact]
    public async Task The_last_paid_place_can_still_be_filled()
    {
        _held = 2;

        (await Assign("user-c")).Value!.Assigned.Should().ContainSingle(
            because: "an off-by-one here would leave an organization paying for a place it could " +
                     "never use");
    }

    [Fact]
    public async Task A_plan_with_no_quantity_item_is_for_exactly_one_person()
    {
        _subscription = UserWise(seats: null);
        _held = 1;

        (await Assign("user-b")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "a user-wise plan with nothing to count is one person's plan, and " +
                         "treating it as unlimited would put a whole organization on one sale");
    }

    [Fact]
    public async Task The_marked_quantity_is_the_one_that_counts_people()
    {
        _subscription = UserWise(seats: 3, countsMembers: true);
        _subscription.QuantityItems.Add(Workspaces(marked: false));
        _held = 3;

        (await Assign("user-d")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "three places were sold — counting the fifty workspaces as people would " +
                         "hand the plan to forty-seven of them free");
    }

    [Fact]
    public async Task A_plan_that_does_not_say_which_quantity_counts_people_is_refused()
    {
        _subscription = UserWise(seats: 3);
        _subscription.QuantityItems.Add(Workspaces(marked: false));

        (await Assign("user-b")).FailureKind.Should().Be(PaymentFailureKind.Validation,
            because: "defaulting to one would put a single person on a subscription charged for " +
                     "three, and nothing would fail until the second could not work");
    }

    [Fact]
    public async Task A_plan_marking_two_quantities_as_people_is_refused()
    {
        _subscription = UserWise(seats: 3, countsMembers: true);
        _subscription.QuantityItems.Add(Workspaces(marked: true));

        (await Assign("user-b")).FailureKind.Should().Be(PaymentFailureKind.Validation,
            because: "whichever of the two was picked would be arbitrary, and one of them gives " +
                     "away forty-seven places");
    }

    [Fact]
    public async Task A_single_quantity_needs_no_mark()
    {
        _subscription = UserWise(seats: 3);
        _held = 2;

        (await Assign("user-c")).Value!.Assigned.Should().ContainSingle(
            because: "there is nothing to disambiguate, which is what keeps every plan authored " +
                     "before this working untouched");
    }

    /// <remarks>
    /// The shape live plans already use: one charge however many people there are, a ceiling of
    /// ten, and a quantity nobody has ever touched because changing it costs nothing.
    /// </remarks>
    [Fact]
    public async Task A_flat_priced_plan_is_for_as_many_people_as_its_ceiling_allows()
    {
        _subscription = UserWise(seats: 1, maxQuantity: 10, pricedPerMember: false);
        _held = 9;

        (await Assign("user-j")).Value!.Assigned.Should().ContainSingle(
            because: "115 CHF bought the plan for up to ten people — reading the untouched " +
                     "quantity of one would give nine of them nothing they paid for");
    }

    [Fact]
    public async Task A_flat_priced_plan_still_stops_at_its_ceiling()
    {
        _subscription = UserWise(seats: 1, maxQuantity: 10, pricedPerMember: false);
        _held = 10;

        (await Assign("user-k")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "up to ten is a ceiling, not an opening offer");
    }

    [Fact]
    public async Task A_flat_priced_plan_with_no_ceiling_is_refused_rather_than_unlimited()
    {
        _subscription = UserWise(seats: 1, maxQuantity: null, pricedPerMember: false);

        (await Assign("user-b")).FailureKind.Should().Be(PaymentFailureKind.Validation,
            because: "unlimited is the honest reading of a flat fee with no cap, and also the " +
                     "one where forgetting to set a maximum gives the product away");
    }

    [Fact]
    public async Task A_per_member_price_is_for_what_was_bought_not_the_ceiling()
    {
        _subscription = UserWise(seats: 3, maxQuantity: 10, pricedPerMember: true);
        _held = 3;

        (await Assign("user-d")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "three were paid for at a price per person, so the plan's ceiling of " +
                         "ten is what could be bought, not what was");
    }

    [Fact]
    public async Task An_organizations_own_subscription_takes_no_members()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Plan.SubscriberScope = SubscriberScope.Organization;

        (await Assign("user-b")).FailureKind.Should().Be(PaymentFailureKind.Validation,
            because: "it is reached through the organization, so putting one person on it would " +
                     "give them what everybody shares");
    }

    [Fact]
    public async Task A_subscription_that_grants_nothing_cannot_take_members()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Status = SubscriptionStatus.Canceled;

        (await Assign("user-b")).FailureKind.Should().Be(PaymentFailureKind.Conflict,
            because: "it would grant nothing, and telling an administrator it worked sends them " +
                     "away believing somebody has access");
    }

    [Fact]
    public async Task A_member_can_still_be_taken_off_after_the_subscription_lapses()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Status = SubscriptionStatus.Canceled;

        (await Service().ReleaseAsync(
                SubscriptionId, "user-b", "corr-1", CancellationToken.None))
            .IsSuccess.Should().BeTrue(
                because: "somebody who has left still has to be taken off a lapsed subscription, " +
                         "and refusing would strand the record of who was on it");
    }

    [Fact]
    public async Task Another_organizations_subscription_cannot_take_members_by_naming_it()
    {
        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-elsewhere", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        (await Service().AssignAsync(
                "sub-elsewhere", new AssignMemberRequest { UserIds = ["user-b"] },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.NotFound,
                because: "the subscription is read through the caller's own organization, so an " +
                         "identifier alone reaches nothing");
    }

    [Fact]
    public async Task Naming_somebody_already_on_it_is_reported_rather_than_doubled()
    {
        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MemberAssignmentOutcome.AlreadyHeld);

        (await Assign("user-b")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_already_assigned",
                because: "reporting it as a fresh assignment would let a caller counting " +
                         "successes believe two places were spent on one person");
    }

    [Fact]
    public async Task A_request_naming_nobody_is_refused()
    {
        (await Service().AssignAsync(
                SubscriptionId, new AssignMemberRequest { UserIds = ["  "] },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task Assigning_a_member_drops_what_the_organization_had_cached()
    {
        var cache = new Mock<IEntitlementSnapshotCache>();

        await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            cache.Object, _time)
            .AssignAsync(
                SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] },
                "corr-1", CancellationToken.None);

        cache.Verify(
            entries => entries.Invalidate(TenantId, OrganizationId),
            Times.Once,
            "the new holder would otherwise wait out the cache before reaching what was bought " +
            "for them");
    }

    private async Task<SubscriptionOperationResult<SubscriptionMemberAssignmentResponse>> Assign(
        params string[] userIds) =>
        await Service().AssignAsync(
            SubscriptionId,
            new AssignMemberRequest { UserIds = [.. userIds] },
            "corr-1",
            CancellationToken.None);

    private SubscriptionMemberService Service() => new(
        _subscriptions.Object,
        _assignments.Object,
        _contextResolver.Object,
        new EntitlementSnapshotCache(new OptionsStub(), _time),
        _time);

    private static SubscriptionQuantityItem Workspaces(bool marked) => new()
    {
        ItemKey = "workspace",
        UnitLabel = "workspace",
        Quantity = 50,
        CountsMembers = marked
    };

    /// <summary>
    /// A user-wise subscription. <paramref name="pricedPerMember"/> is the difference between the
    /// two pricing modes: a flat price charges once however many people there are, a per-member
    /// price multiplies by how many were bought.
    /// </summary>
    private static SubscriptionDetail UserWise(
        long? seats,
        bool countsMembers = false,
        long? maxQuantity = null,
        bool pricedPerMember = true) => new()
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            Status = SubscriptionStatus.Active,
            CurrencyCode = "CHF",
            CurrentPeriodEndUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            QuantityItems = seats is null
                ? []
                : [new SubscriptionQuantityItem
                {
                    ItemKey = "seat",
                    UnitLabel = "seat",
                    Quantity = seats.Value,
                    CountsMembers = countsMembers,
                    MaxQuantity = maxQuantity
                }],
            Price = new PriceSnapshot
            {
                QuantityItemKey = pricedPerMember ? "seat" : null
            },
            Plan = new PlanSnapshot
            {
                Code = "starter",
                SubscriberScope = SubscriberScope.User
            }
        };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
