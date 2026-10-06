using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Scheduling;
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

        // Held seats rather than a count: the service picks a seat number, so it needs to know
        // which ones are taken. Seats fill from one upwards, as assignment hands them out.
        _assignments
            .Setup(repository => repository.ListActiveAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [.. Enumerable.Range(1, (int)_held)
                .Select(seat => new SubscriptionAssignment
                {
                    TenantId = TenantId,
                    OrganizationId = OrganizationId,
                    SubscriptionId = SubscriptionId,
                    UserId = $"held-{seat}",
                    SeatNumber = seat
                })]);

        // Nobody holds a place anywhere else unless a test says so.
        _assignments
            .Setup(repository => repository.ListSeatsForUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

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
            .ReturnsAsync(new SubscriptionAssignment { SeatNumber = 1 });
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


    [Fact]
    public async Task People_are_given_distinct_seats()
    {
        var written = new List<SubscriptionAssignment>();

        _subscription = UserWise(seats: 3);
        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAssignment, CancellationToken>((a, _) => written.Add(a))
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        await Assign("u1", "u2", "u3");

        written.Select(assignment => assignment.SeatNumber)
            .Should().BeEquivalentTo([1, 2, 3],
                because: "a seat is what carries an allowance, so two people on one seat would " +
                         "share what was bought for one of them");
    }

    /// <summary>
    /// Found testing in the portal: re-sending a list on a full subscription told the administrator
    /// the subscription was out of room — for someone who already had a place on it.
    /// </summary>
    [Fact]
    public async Task Somebody_already_on_a_full_subscription_is_reported_as_already_on_it()
    {
        _subscription = UserWise(seats: 3);
        _held = 3;

        var result = await Assign("held-2");

        result.Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_already_assigned",
                because: "they are on it; telling the administrator to buy another seat for them " +
                         "sends them after a problem that does not exist");
    }

    [Fact]
    public async Task A_name_already_on_it_does_not_use_up_a_free_place_for_the_next_name()
    {
        _subscription = UserWise(seats: 3);
        _held = 2;

        var result = await Assign("held-1", "newcomer");

        result.Value!.Assigned.Should().ContainSingle().Which.UserId.Should().Be("newcomer");
        result.Value.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_already_assigned");
    }

    [Fact]
    public async Task The_member_list_says_which_place_each_person_holds()
    {
        _subscription = UserWise(seats: 3);
        _held = 2;

        var result = await Service().ListAsync(SubscriptionId, "corr-1", CancellationToken.None);

        result.Value!.Seats.Select(seat => seat.SeatNumber).Should().Equal([1, 2],
            because: "a list of names without places cannot show which place is empty, and the " +
                     "allowance belongs to the place rather than the person");
    }

    [Fact]
    public async Task A_newcomer_takes_a_seat_nobody_is_on()
    {
        SubscriptionAssignment? written = null;

        _subscription = UserWise(seats: 3);
        _held = 2;
        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAssignment, CancellationToken>((a, _) => written = a)
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        await Assign("u3");

        written!.SeatNumber.Should().Be(3,
            because: "seats one and two are occupied, and putting a third person on one of them " +
                     "would hand them somebody else's remaining allowance");
    }

    /// <remarks>
    /// The reason a seat is numbered at all. Were the allowance attached to the person rather than
    /// the seat, an organization could release somebody who had spent their window, assign
    /// somebody else, and start again — minting usage without limit from one paid seat.
    /// </remarks>
    [Fact]
    public async Task A_freed_seat_is_handed_out_again_rather_than_a_new_one_invented()
    {
        SubscriptionAssignment? written = null;

        _subscription = UserWise(seats: 3);

        // Seat 2 was given up; one and three are still held.
        _assignments
            .Setup(repository => repository.ListActiveAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Held("u1", seat: 1),
                Held("u3", seat: 3)
            ]);

        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAssignment, CancellationToken>((a, _) => written = a)
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);

        await Assign("u2");

        written!.SeatNumber.Should().Be(2,
            because: "the freed seat is the one to reuse — inventing a fourth would let an " +
                     "organization cycle people through three paid seats forever");
    }

    [Fact]
    public async Task Seats_a_scheduled_decrease_will_remove_are_not_filled()
    {
        _subscription = UserWise(seats: 5);
        _subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities =
            [
                new SubscriptionQuantityItem { ItemKey = "seat", Quantity = 2 }
            ]
        };
        _held = 2;

        (await Assign("u3")).Value!.Refused.Should().ContainSingle()
            .Which.ReasonCode.Should().Be("subscription_member_limit_reached",
                because: "a decrease is not refunded, so it will take effect — filling a seat it " +
                         "removes strands somebody the moment the period turns over");
    }

    private static PendingQuantityChange CutTo(long seats) => new()
    {
        RequestedQuantities = [new SubscriptionQuantityItem { ItemKey = "seat", Quantity = seats }],
        EffectiveAtUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// Found testing in the portal: with a cut from five to four scheduled and four people on, the
    /// list said "4 of 5" and showed place 5 empty, and assigning to it was then refused. Both have
    /// to read the same count.
    /// </summary>
    [Fact]
    public async Task The_member_list_counts_a_scheduled_decrease_as_assignment_does()
    {
        _subscription = UserWise(seats: 5);
        _subscription.PendingQuantityChange = CutTo(4);
        _held = 4;

        var list = (await Service().ListAsync(SubscriptionId, "corr-1", CancellationToken.None)).Value!;

        list.Purchased.Should().Be(5, "the fifth place is still paid for until the period ends");
        list.Available.Should().Be(0,
            because: "the fifth place is going, so there is nothing an assignment could fill");
        list.ScheduledPlaces.Should().Be(4);
        list.ScheduledAtUtc.Should().Be(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_member_list_with_no_decrease_scheduled_says_nothing_of_one()
    {
        _subscription = UserWise(seats: 5);
        _held = 4;

        var list = (await Service().ListAsync(SubscriptionId, "corr-1", CancellationToken.None)).Value!;

        list.Available.Should().Be(1);
        list.ScheduledPlaces.Should().BeNull();
        list.ScheduledAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_place_closed_by_a_scheduled_decrease_is_refused_for_that_reason()
    {
        _subscription = UserWise(seats: 5);
        _subscription.PendingQuantityChange = CutTo(4);
        _held = 4;

        (await Assign("newcomer")).Value!.Refused.Should().ContainSingle()
            .Which.Reason.Should().Be(
                "This subscription drops to 4 places on 2026-12-31, and every one of them is taken.",
                because: "it was bought for five and has four — \"all the people it was bought " +
                         "for\" sent the administrator looking for a fifth");
    }

    /// <summary>
    /// A flat-priced plan's places are its maximum. Lowering its quantity changes neither the price
    /// nor the places, so it must not close any.
    /// </summary>
    [Fact]
    public async Task A_quantity_decrease_on_a_flat_priced_plan_closes_no_places()
    {
        _subscription = UserWise(seats: 5, maxQuantity: 10, pricedPerMember: false);
        _subscription.PendingQuantityChange = CutTo(2);
        _held = 4;

        var result = await Assign("newcomer");

        result.Value!.Assigned.Should().ContainSingle(
            because: "ten places were sold whatever the quantity says, and four are taken");
    }

    private static SubscriptionAssignment Held(string userId, int seat) => new()
    {
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = SubscriptionId,
        UserId = userId,
        SeatNumber = seat
    };

    private async Task<SubscriptionOperationResult<SubscriptionMemberAssignmentResponse>> Assign(
        params string[] userIds) =>
        await Service().AssignAsync(
            SubscriptionId,
            new AssignMemberRequest { UserIds = [.. userIds] },
            "corr-1",
            CancellationToken.None);

    /// <remarks>
    /// The person's own question, which no endpoint answered: <c>current</c> never returns a
    /// user-wise subscription, and finding oneself in every roster needs the administrator's
    /// permission and a call per subscription.
    /// </remarks>
    [Fact]
    public async Task Mine_lists_the_live_subscriptions_the_caller_holds_a_place_on_with_the_place()
    {
        _subscription.Plan.Code = "u_t_4";
        _assignments
            .Setup(repository => repository.ListSeatsForUserAsync(
                TenantId, OrganizationId, "admin-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HeldSeat(SubscriptionId, 2), new HeldSeat("sub-ended", 1)]);
        _subscriptions
            .Setup(repository => repository.ListLiveByIdsAsync(
                TenantId,
                It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 2),
                _time.GetUtcNow().UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([_subscription]);

        var result = await Service().ListMineAsync(null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var place = result.Value!.Should().ContainSingle(
            "a place on a subscription that has ended grants nothing, so it is not listed").Subject;
        place.SubscriptionId.Should().Be(SubscriptionId);
        place.PlanCode.Should().Be("u_t_4");
        place.SeatNumber.Should().Be(2);
    }

    [Fact]
    public async Task Mine_is_empty_for_someone_holding_no_place()
    {
        _assignments
            .Setup(repository => repository.ListSeatsForUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Service().ListMineAsync(null, "corr-1", CancellationToken.None);

        result.Value.Should().BeEmpty();
        _subscriptions.Verify(
            repository => repository.ListLiveByIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Assigning_names_the_new_holder_on_the_places_usage_row()
    {
        var current = new Mock<ISubscriptionUsageCurrentRepository>();

        var result = await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            new EntitlementSnapshotCache(new OptionsStub(), _time), _time, current: current.Object)
            .AssignAsync(
                SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] },
                "corr-1", CancellationToken.None);

        var seat = result.Value!.Assigned.Should().ContainSingle().Subject.SeatNumber!.Value;
        current.Verify(
            repository => repository.SetSeatHolderAsync(
                TenantId, SubscriptionId, seat, "user-b", It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <remarks>
    /// Found on dev: a place nobody had recorded on had no usage row, so naming its holder updated
    /// nothing and the new member's allowance stayed invisible until their first use.
    /// </remarks>
    [Fact]
    public async Task Assigning_schedules_a_projection_refresh_that_seeds_the_new_place()
    {
        var scheduler = new Mock<ISubscriptionWorkScheduler>();

        await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            new EntitlementSnapshotCache(new OptionsStub(), _time), _time, scheduler: scheduler.Object)
            .AssignAsync(
                SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] },
                "corr-1", CancellationToken.None);

        scheduler.Verify(
            work => work.ScheduleUsageProjectionRefreshAsync(
                TenantId, It.IsAny<string>(), SubscriptionId, "corr-1",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "renaming the holder only touches a row that exists; the refresh is what creates one");
    }

    /// <remarks>Found on dev: the answer said place null, assigned 0001-01-01.</remarks>
    [Fact]
    public async Task A_release_answers_with_the_place_given_back()
    {
        var assignedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        _assignments
            .Setup(repository => repository.TryReleaseAsync(
                TenantId, SubscriptionId, "user-b", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionAssignment { SeatNumber = 2, AssignedAtUtc = assignedAt });

        var released = (await Service().ReleaseAsync(
            SubscriptionId, "user-b", "corr-1", CancellationToken.None)).Value!;

        released.SeatNumber.Should().Be(2);
        released.AssignedAtUtc.Should().Be(assignedAt);
    }

    [Fact]
    public async Task Releasing_clears_the_holder_from_the_places_usage_row()
    {
        var current = new Mock<ISubscriptionUsageCurrentRepository>();

        await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            new EntitlementSnapshotCache(new OptionsStub(), _time), _time, current: current.Object)
            .ReleaseAsync(SubscriptionId, "user-b", "corr-1", CancellationToken.None);

        current.Verify(
            repository => repository.ClearSeatHolderAsync(
                TenantId, SubscriptionId, "user-b", It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_holder_that_cannot_be_written_does_not_fail_the_assignment()
    {
        var current = new Mock<ISubscriptionUsageCurrentRepository>();
        current
            .Setup(repository => repository.SetSeatHolderAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo is away"));

        var result = await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            new EntitlementSnapshotCache(new OptionsStub(), _time), _time, current: current.Object)
            .AssignAsync(
                SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] },
                "corr-1", CancellationToken.None);

        result.Value!.Assigned.Should().ContainSingle(
            "the assignment is the record; the usage row is a read model the next recording repairs");
    }

    /// <remarks>
    /// Where a person's own allowance and paces are shown beside the place they hold. Only the
    /// current window: a row for a window that has closed describes a balance nobody can spend.
    /// </remarks>
    [Fact]
    public async Task The_roster_carries_each_places_current_usage_and_paces()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var current = new Mock<ISubscriptionUsageCurrentRepository>();
        current
            .Setup(repository => repository.ListBySubscriptionAsync(
                TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionUsageCurrent>)
            [
                PlaceRow(seat: 1, "user-a", now.AddDays(-1), now.AddDays(1), used: 7),
                PlaceRow(seat: 2, "user-b", now.AddDays(-31), now.AddDays(-1), used: 99),
                // The organization's own row: not a place's.
                new SubscriptionUsageCurrent
                {
                    SubscriptionId = SubscriptionId, MeterKey = "tokens",
                    PeriodStartUtc = now.AddDays(-1), PeriodEndUtc = now.AddDays(1)
                }
            ]);

        var result = await new SubscriptionMemberService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            new EntitlementSnapshotCache(new OptionsStub(), _time), _time, current: current.Object)
            .ListAsync(SubscriptionId, "corr-1", CancellationToken.None);

        var usage = result.Value!.Usage.Should().ContainSingle(
            "a closed window and the organization's own row are not a place's current usage").Subject;
        usage.SeatNumber.Should().Be(1);
        usage.UserId.Should().Be("user-a");
        usage.Used.Should().Be(7);
        usage.SubLimits.Should().ContainSingle().Which.Window.Should().Be("Hour");
    }

    private static SubscriptionUsageCurrent PlaceRow(
        int seat, string userId, DateTime start, DateTime end, decimal used) => new()
    {
        SubscriptionId = SubscriptionId,
        SeatNumber = seat,
        UserId = userId,
        MeterKey = "tokens",
        PeriodStartUtc = start,
        PeriodEndUtc = end,
        Included = 100,
        Used = used,
        Remaining = 100 - used,
        SubLimits =
        [
            new SubscriptionUsageCurrentSubLimit
            {
                Window = UsageWindow.Hour, WindowCount = 5, Rolling = true, Quantity = 10, Used = 3
            }
        ]
    };

    /// <remarks>
    /// Found testing on dev: a person on two user-wise plans that both meter tokens had every use
    /// go to one of them, with the other's paces shown beside it.
    /// </remarks>
    [Fact]
    public async Task A_person_already_on_a_plan_metering_the_same_usage_is_refused_a_second_place()
    {
        _subscription.Plan.Meters = [new PlanMeter { MeterKey = "token", DisplayName = "Tokens" }];
        var other = UserWise(seats: 2);
        other.ItemId = "sub-other";
        other.Plan.DisplayName = "user-test-4";
        other.Plan.Meters = [new PlanMeter { MeterKey = "token", DisplayName = "Tokens" }];
        OnlyOtherPlace("user-b", other);

        var result = await Service().AssignAsync(
            SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] }, "corr-1",
            CancellationToken.None);

        var refusal = result.Value!.Refused.Should().ContainSingle().Subject;
        refusal.ReasonCode.Should().Be("subscription_member_meter_overlap");
        refusal.Reason.Should().Contain("user-test-4");
        result.Value.Assigned.Should().BeEmpty();
    }

    [Fact]
    public async Task A_place_on_a_plan_metering_different_usage_does_not_stand_in_the_way()
    {
        _subscription.Plan.Meters = [new PlanMeter { MeterKey = "token", DisplayName = "Tokens" }];
        var other = UserWise(seats: 2);
        other.ItemId = "sub-other";
        other.Plan.Meters = [new PlanMeter { MeterKey = "call", DisplayName = "Calls" }];
        OnlyOtherPlace("user-b", other);

        var result = await Service().AssignAsync(
            SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] }, "corr-1",
            CancellationToken.None);

        result.Value!.Assigned.Should().ContainSingle();
    }

    private void OnlyOtherPlace(string userId, SubscriptionDetail other)
    {
        _assignments
            .Setup(repository => repository.ListSeatsForUserAsync(
                TenantId, OrganizationId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HeldSeat(other.ItemId, 1)]);
        _subscriptions
            .Setup(repository => repository.ListLiveByIdsAsync(
                TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([other]);
    }

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
