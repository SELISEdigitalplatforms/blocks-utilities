using System.Text.Json;
using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The email events a seat change writes for the person it concerns (spec 001, AC-5 to AC-7).
/// </summary>
/// <remarks>
/// Guards against a member never being told they gained or lost access, being told twice, or
/// being told about a seat that was never written. The event goes in before the seat, so a crash
/// between the two can only leave an event the mail later drops, never a silent seat change.
/// </remarks>
public sealed class SubscriptionMemberNotificationTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";
    private const string SubscriptionId = "sub-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionAssignmentRepository> _assignments = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<IMemberDirectory> _directory = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private readonly List<string> _writes = [];
    private readonly List<SubscriptionOutboxEvent> _appended = [];
    private List<SubscriptionAssignment> _held = [];

    public SubscriptionMemberNotificationTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(
                    TenantId, OrganizationId, "actor-1", "admin-1", "Grace Hopper", "grace@example.com")));
        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => UserWise(seats: 3));
        _subscriptions
            .Setup(repository => repository.TryAppendEventAsync(
                TenantId, SubscriptionId, It.IsAny<SubscriptionOutboxEvent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionOutboxEvent, CancellationToken>((_, _, outboxEvent, _) =>
            {
                _writes.Add("event");
                _appended.Add(outboxEvent);
            })
            .ReturnsAsync(true);
        _assignments
            .Setup(repository => repository.ListActiveAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _held);
        _assignments
            .Setup(repository => repository.ListSeatsForUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _assignments
            .Setup(repository => repository.TryAssignAsync(
                It.IsAny<SubscriptionAssignment>(), It.IsAny<CancellationToken>()))
            .Callback(() => _writes.Add("seat"))
            .ReturnsAsync(MemberAssignmentOutcome.Assigned);
        _assignments
            .Setup(repository => repository.TryReleaseAsync(
                TenantId, SubscriptionId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => _writes.Add("seat"))
            .ReturnsAsync(new SubscriptionAssignment { SeatNumber = 1 });
        _directory
            .Setup(directory => directory.FindUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, CancellationToken _) =>
                new MemberContact($"{userId}@example.com", $"Name of {userId}", "de-CH"));
        _directory
            .Setup(directory => directory.FindOrganizationNameAsync(OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Analytical Engines Ltd");
    }

    [Fact]
    public async Task Assigning_writes_the_members_email_event_before_their_seat()
    {
        await Service().AssignAsync(
            SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] }, "corr-1", CancellationToken.None);

        _writes.Should().Equal(["event", "seat"],
            "written after the seat, a crash between the two would leave a seat nobody is told about");

        var payload = Payload(_appended.Single());
        payload.EventType.Should().Be(SubscriptionConstants.SubscriptionMemberAssigned);
        payload.MemberUserId.Should().Be("user-b");
        payload.MemberEmail.Should().Be("user-b@example.com");
        payload.MemberDisplayName.Should().Be("Name of user-b");
        payload.MemberLanguage.Should().Be("de-CH", "the member reads it in their own language");
        payload.OrganizationName.Should().Be("Analytical Engines Ltd");
        payload.ActorName.Should().Be("Grace Hopper");
        payload.AssignmentId.Should().NotBeNullOrWhiteSpace(
            "the mail checks this seat before sending, since the event is written first");
    }

    [Fact]
    public async Task Somebody_already_seated_is_neither_looked_up_nor_announced()
    {
        _held = [Held("user-a", seat: 1)];

        await Service().AssignAsync(
            SubscriptionId, new AssignMemberRequest { UserIds = ["user-a", "user-b"] },
            "corr-1", CancellationToken.None);

        _appended.Select(outboxEvent => Payload(outboxEvent).MemberUserId).Should().Equal(
            ["user-b"], "telling someone who already had a seat that they were added is noise");
        _directory.Verify(
            directory => directory.FindUserAsync("user-a", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_member_iam_cannot_describe_is_still_seated_with_an_event_carrying_no_address()
    {
        _directory
            .Setup(directory => directory.FindUserAsync("user-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemberContact?)null);

        var result = await Service().AssignAsync(
            SubscriptionId, new AssignMemberRequest { UserIds = ["user-b"] }, "corr-1", CancellationToken.None);

        result.Value!.Assigned.Should().ContainSingle(
            "the email is a courtesy; an IAM outage must not stop an administrator seating people");
        Payload(_appended.Single()).MemberEmail.Should().BeNull();
    }

    [Fact]
    public async Task Releasing_announces_the_seat_being_closed_before_closing_it()
    {
        var holding = Held("user-b", seat: 2);
        _assignments
            .Setup(repository => repository.GetActiveAsync(
                TenantId, SubscriptionId, "user-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(holding);

        await Service().ReleaseAsync(SubscriptionId, "user-b", "corr-1", CancellationToken.None);

        _writes.Should().Equal(["event", "seat"]);
        var payload = Payload(_appended.Single());
        payload.EventType.Should().Be(SubscriptionConstants.SubscriptionMemberReleased);
        payload.AssignmentId.Should().Be(holding.ItemId,
            "the mail checks that this very seat was released before telling anyone");
    }

    [Fact]
    public async Task Releasing_somebody_with_no_seat_announces_nothing()
    {
        await Service().ReleaseAsync(SubscriptionId, "user-x", "corr-1", CancellationToken.None);

        _appended.Should().BeEmpty("there is no seat change to tell anyone about");
    }

    private SubscriptionMemberService Service() => new(
        _subscriptions.Object,
        _assignments.Object,
        _contextResolver.Object,
        _cache.Object,
        _time,
        events: new SubscriptionOutboxEventFactory(),
        directory: _directory.Object);

    private static SubscriptionAssignment Held(string userId, int seat) => new()
    {
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = SubscriptionId,
        UserId = userId,
        SeatNumber = seat
    };

    private static SubscriptionLifecycleEvent Payload(SubscriptionOutboxEvent outboxEvent) =>
        JsonSerializer.Deserialize<SubscriptionLifecycleEvent>(
            outboxEvent.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static SubscriptionDetail UserWise(long seats) => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        QuantityItems =
        [
            new SubscriptionQuantityItem { ItemKey = "seat", UnitLabel = "seat", Quantity = seats }
        ],
        Price = new PriceSnapshot { QuantityItemKey = "seat" },
        Plan = new PlanSnapshot
        {
            Code = "starter",
            DisplayName = "Starter",
            SubscriberScope = SubscriberScope.User
        }
    };
}
