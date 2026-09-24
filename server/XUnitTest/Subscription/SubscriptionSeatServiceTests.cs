using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Who may be seated, and on what.
/// </summary>
/// <remarks>
/// Guards giving away access nobody paid for. Seats are what a subscription was charged for, so
/// seating more people than were bought hands out the plan free; seating anyone on an
/// organization's own subscription would hand the whole organization's plan to one person; and
/// seating somebody on a subscription that no longer grants anything sells them nothing.
/// </remarks>
public sealed class SubscriptionSeatServiceTests
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

    public SubscriptionSeatServiceTests()
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
            .ReturnsAsync(SeatAssignmentOutcome.Assigned);

        _assignments
            .Setup(repository => repository.TryReleaseAsync(
                TenantId, SubscriptionId, It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeatReleaseOutcome.Released);
    }

    [Fact]
    public async Task A_seat_goes_to_the_person_the_request_names_not_the_caller()
    {
        SubscriptionAssignment? written = null;

        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAssignment, CancellationToken>((a, _) => written = a)
            .ReturnsAsync(SeatAssignmentOutcome.Assigned);

        await Service().AssignAsync(
            SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
            "corr-1", CancellationToken.None);

        written!.UserId.Should().Be("user-b",
            because: "an administrator fills seats for other people, so taking the holder from " +
                     "the token would seat the administrator on every one of them");
        written.AssignedByUserId.Should().Be("admin-1",
            because: "who handed out the seat is what an audit asks about later");
    }

    [Fact]
    public async Task Seating_more_people_than_were_paid_for_is_refused()
    {
        _held = 3;

        var result = await Service().AssignAsync(
            SubscriptionId, new AssignSeatRequest { UserId = "user-d" },
            "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict,
            because: "seats are what the subscription was charged for, so seating a fourth " +
                     "person on three paid seats gives the plan away");
        _assignments.Verify(
            repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_last_paid_seat_can_still_be_filled()
    {
        _held = 2;

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-c" },
                "corr-1", CancellationToken.None))
            .IsSuccess.Should().BeTrue(
                because: "an off-by-one here would leave an organization paying for a seat it " +
                         "could never use");
    }

    [Fact]
    public async Task A_plan_with_no_quantity_item_sells_exactly_one_seat()
    {
        _subscription = UserWise(seats: null);
        _held = 1;

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Conflict,
                because: "a user-wise plan with nothing to count is one person's plan, and " +
                         "treating it as unlimited would seat a whole organization on one sale");
    }

    [Fact]
    public async Task A_plan_that_does_not_say_which_quantity_counts_people_is_refused()
    {
        _subscription = UserWise(seats: 3);
        _subscription.QuantityItems.Add(new SubscriptionQuantityItem
        {
            ItemKey = "workspace",
            UnitLabel = "workspace",
            Quantity = 10
        });

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Validation,
                because: "nothing marks a quantity item as the one that counts people, and " +
                         "guessing would seat them against a figure sold as something else");
    }

    [Fact]
    public async Task An_organizations_own_subscription_has_no_seats_to_give_out()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Plan.SubscriberScope = SubscriberScope.Organization;

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Validation,
                because: "it is reached through the organization, so seating one person on it " +
                         "would hand them what everybody shares");
    }

    [Fact]
    public async Task A_subscription_that_grants_nothing_cannot_be_seated()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Status = SubscriptionStatus.Canceled;

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Conflict,
                because: "the seat would grant nothing, and telling an administrator it worked " +
                         "sends them away believing somebody has access");
    }

    [Fact]
    public async Task A_seat_can_still_be_taken_back_after_the_subscription_lapses()
    {
        _subscription = UserWise(seats: 3);
        _subscription.Status = SubscriptionStatus.Canceled;

        (await Service().ReleaseAsync(
                SubscriptionId, "user-b", "corr-1", CancellationToken.None))
            .IsSuccess.Should().BeTrue(
                because: "somebody who has left still has to be taken off a lapsed subscription, " +
                         "and refusing would strand the record of who held it");
    }

    [Fact]
    public async Task Another_organizations_subscription_cannot_be_seated_by_naming_it()
    {
        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-elsewhere", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        (await Service().AssignAsync(
                "sub-elsewhere", new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.NotFound,
                because: "the subscription is read through the caller's own organization, so an " +
                         "identifier alone reaches nothing");
    }

    [Fact]
    public async Task Seating_somebody_who_already_holds_a_seat_is_reported_rather_than_doubled()
    {
        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeatAssignmentOutcome.AlreadyHeld);

        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Conflict,
                because: "reporting it as a fresh assignment would let a caller counting " +
                         "successes believe two seats were spent on one person");
    }

    [Fact]
    public async Task A_request_naming_nobody_is_refused()
    {
        (await Service().AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "  " },
                "corr-1", CancellationToken.None))
            .FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task Filling_a_seat_drops_what_the_organization_had_cached()
    {
        var cache = new Mock<IEntitlementSnapshotCache>();

        await new SubscriptionSeatService(
            _subscriptions.Object, _assignments.Object, _contextResolver.Object,
            cache.Object, _time)
            .AssignAsync(
                SubscriptionId, new AssignSeatRequest { UserId = "user-b" },
                "corr-1", CancellationToken.None);

        cache.Verify(
            entries => entries.Invalidate(TenantId, OrganizationId),
            Times.Once,
            "the new holder would otherwise wait out the cache before reaching what was bought " +
            "for them");
    }

    private SubscriptionSeatService Service() => new(
        _subscriptions.Object,
        _assignments.Object,
        _contextResolver.Object,
        new EntitlementSnapshotCache(new OptionsStub(), _time),
        _time);

    private static SubscriptionDetail UserWise(long? seats) => new()
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
                Quantity = seats.Value
            }],
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
