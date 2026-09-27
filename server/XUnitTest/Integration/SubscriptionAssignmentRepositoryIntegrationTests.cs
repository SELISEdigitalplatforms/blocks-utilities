using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// Who holds which seat, where only a real database can settle it.
/// </summary>
/// <remarks>
/// Guards an organization being charged for seats it cannot use and people keeping access they gave
/// up. Both turn on a partial unique index rather than on anything this code checks: two
/// administrators assigning the same person at once would both pass a read, and a seat handed back
/// and handed out again would have nowhere to insert if released rows still occupied the index.
/// <para>
/// The filter matches BSON null, so an active row must really carry <c>ReleasedAtUtc: null</c>
/// rather than omitting the field — a document that omitted it would sit outside the index and be
/// exempt from the rule. That invariant is asserted directly here, because nothing in the type
/// system holds it.
/// </para>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionAssignmentRepositoryIntegrationTests
{
    private const string OrganizationId = "org-1";

    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionAssignmentRepository _assignments;

    public SubscriptionAssignmentRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _assignments = new SubscriptionAssignmentRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_seat_is_given_to_the_person_named()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcome = await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None);

        outcome.Should().Be(MemberAssignmentOutcome.Assigned);

        (await _assignments.ListSeatsForUserAsync(
                tenantId, OrganizationId, "user-a", CancellationToken.None))
            .Should().ContainSingle().Which.SubscriptionId.Should().Be("sub-a",
                because: "entitlement reads this to find what a person may use, so a seat that " +
                         "does not come back here grants them nothing they have paid for");
    }

    [Fact]
    public async Task An_active_seat_really_stores_a_null_release_rather_than_omitting_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var assignment = NewAssignment(tenantId, "sub-null", "user-a");

        await _assignments.TryAssignAsync(assignment, CancellationToken.None);

        var stored = await _fixture.Collection<BsonDocument>("SubscriptionAssignments")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", assignment.ItemId))
            .FirstAsync(CancellationToken.None);

        stored.Contains(nameof(SubscriptionAssignment.ReleasedAtUtc)).Should().BeTrue(
            because: "the unique index filters on this field being null, so a row that omitted " +
                     "it would fall outside the index and escape the one-seat-per-person rule");
        stored[nameof(SubscriptionAssignment.ReleasedAtUtc)].IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public async Task The_same_person_cannot_take_a_second_seat_on_one_subscription()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None);

        (await _assignments.TryAssignAsync(
                NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None))
            .Should().Be(MemberAssignmentOutcome.AlreadyHeld,
                because: "a caller that counts seats off successful assignments would otherwise " +
                         "consume two of them for one person");
    }

    [Fact]
    public async Task Only_one_of_two_administrators_assigning_at_once_takes_the_seat()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcomes = await Task.WhenAll(
            _assignments.TryAssignAsync(
                NewAssignment(tenantId, "sub-race", "user-a", seat: 1), CancellationToken.None),
            _assignments.TryAssignAsync(
                NewAssignment(tenantId, "sub-race", "user-a", seat: 1), CancellationToken.None));

        outcomes.Count(outcome => outcome == MemberAssignmentOutcome.Assigned)
            .Should().Be(1,
                because: "both would pass a read-then-write, and the subscription would look one " +
                         "seat fuller than it is");
    }

    [Fact]
    public async Task One_person_may_hold_members_on_two_different_subscriptions()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-allowance", "user-a"), CancellationToken.None);

        (await _assignments.TryAssignAsync(
                NewAssignment(tenantId, "sub-storage", "user-a"), CancellationToken.None))
            .Should().Be(MemberAssignmentOutcome.Assigned,
                because: "two plans are two purchases — uniqueness is per subscription, and a " +
                         "rule spanning them would refuse a sale the organization is entitled to");
    }

    [Fact]
    public async Task A_seat_handed_back_can_be_given_to_the_same_person_again()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None);
        await _assignments.TryReleaseAsync(
            tenantId, "sub-a", "user-a", DateTime.UtcNow, CancellationToken.None);

        (await _assignments.TryAssignAsync(
                NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None))
            .Should().Be(MemberAssignmentOutcome.Assigned,
                because: "released rows stay for the history of a part-spent period, so they are " +
                         "outside the index — had they counted, the seat could never be reused");
    }

    [Fact]
    public async Task A_released_seat_stops_granting_anything()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None);
        await _assignments.TryReleaseAsync(
            tenantId, "sub-a", "user-a", DateTime.UtcNow, CancellationToken.None);

        (await _assignments.ListSeatsForUserAsync(
                tenantId, OrganizationId, "user-a", CancellationToken.None))
            .Should().BeEmpty(
                because: "someone who gave up their seat keeps access until this stops returning " +
                         "it");
    }

    [Fact]
    public async Task Releasing_a_seat_nobody_holds_reports_it_rather_than_pretending()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _assignments.TryReleaseAsync(
                tenantId, "sub-a", "user-ghost", DateTime.UtcNow, CancellationToken.None))
            .Should().Be(MemberReleaseOutcome.NotHeld);
    }

    [Fact]
    public async Task Releasing_the_same_seat_twice_is_reported_the_second_time()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-a", "user-a"), CancellationToken.None);
        await _assignments.TryReleaseAsync(
            tenantId, "sub-a", "user-a", DateTime.UtcNow, CancellationToken.None);

        (await _assignments.TryReleaseAsync(
                tenantId, "sub-a", "user-a", DateTime.UtcNow, CancellationToken.None))
            .Should().Be(MemberReleaseOutcome.NotHeld,
                because: "the filter already excludes released rows, so a repeat cannot be " +
                         "reported as a fresh release");
    }

    [Fact]
    public async Task Held_members_are_counted_and_released_ones_are_not()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-count", "user-a", seat: 1), CancellationToken.None);
        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-count", "user-b", seat: 2), CancellationToken.None);
        await _assignments.TryReleaseAsync(
            tenantId, "sub-count", "user-a", DateTime.UtcNow, CancellationToken.None);

        (await _assignments.CountActiveAsync(tenantId, "sub-count", CancellationToken.None))
            .Should().Be(1,
                because: "this is the figure a caller reserves the next seat against, and " +
                         "counting a seat that was given back would refuse a sale");
    }

    [Fact]
    public async Task Ending_a_subscription_frees_every_seat_on_it_at_once()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-end", "user-a", seat: 1), CancellationToken.None);
        await _assignments.TryAssignAsync(
            NewAssignment(tenantId, "sub-end", "user-b", seat: 2), CancellationToken.None);

        (await _assignments.ReleaseAllAsync(
                tenantId, "sub-end", DateTime.UtcNow, CancellationToken.None))
            .Should().Be(2);

        (await _assignments.ListActiveAsync(tenantId, "sub-end", CancellationToken.None))
            .Should().BeEmpty(
                because: "a cancelled subscription that left its seats held would keep granting " +
                         "everyone on it whatever the plan allowed");
    }

    [Fact]
    public async Task Another_organizations_members_are_never_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var elsewhere = NewAssignment(tenantId, "sub-other", "user-a");
        elsewhere.OrganizationId = "org-2";
        await _assignments.TryAssignAsync(elsewhere, CancellationToken.None);

        (await _assignments.ListSeatsForUserAsync(
                tenantId, OrganizationId, "user-a", CancellationToken.None))
            .Should().BeEmpty(
                because: "one person can belong to more than one organization, and reading a " +
                         "seat across the boundary grants them another organization's plan");
    }

    /// <remarks>
    /// Refused rather than stored, because the index would otherwise answer for it: every seatless
    /// assignment on a subscription shares seat zero, so the second one comes back AlreadyHeld —
    /// true of the seat and quite wrong about the person.
    /// </remarks>
    [Fact]
    public async Task An_assignment_with_no_seat_is_refused_outright()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seatless = NewAssignment(tenantId, "sub-a", "user-a");
        seatless.SeatNumber = 0;

        await FluentActions
            .Awaiting(() => _assignments.TryAssignAsync(seatless, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>(
                because: "a seat is what carries an allowance, so an assignment without one is " +
                         "not a lesser record but a different thing entirely");
    }

    /// <summary>
    /// One assignment. <paramref name="seat"/> is which of the subscription's seats it occupies —
    /// the thing that carries an allowance, and the thing the unique index keys on, so two people
    /// on one subscription must not share it.
    /// </summary>
    private static SubscriptionAssignment NewAssignment(
        string tenantId,
        string subscriptionId,
        string userId,
        int seat = 1) => new()
        {
            SeatNumber = seat,
            TenantId = tenantId,
            OrganizationId = OrganizationId,
            SubscriptionId = subscriptionId,
            UserId = userId,
            AssignedByUserId = "admin-1",
            CorrelationId = "corr-1"
        };
}
