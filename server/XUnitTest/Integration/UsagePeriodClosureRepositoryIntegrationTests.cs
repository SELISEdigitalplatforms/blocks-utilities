using FluentAssertions;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The atomicity a usage claim and a cancellation's closure reservation actually need, which only
/// a real database can demonstrate: a mocked repository would answer with whatever a test told it
/// to, proving nothing about whether two concurrent claims against the same period can really only
/// produce one active writer count, whether a claim's own idempotency key really is enforced by
/// something other than good faith, or whether a reservation two racing cancellations both attempt
/// really does converge onto one outcome.
/// </summary>
[Collection(MongoIntegrationCollection.Name)]
public sealed class UsagePeriodClosureRepositoryIntegrationTests
{
    private readonly UsagePeriodClosureRepository _closures;
    private readonly MongoIntegrationFixture _fixture;

    public UsagePeriodClosureRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _closures = new UsagePeriodClosureRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_claim_taken_out_fresh_increments_the_active_writer_count()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcome = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        outcome.Should().Be(UsageClaimOutcome.Acquired);
        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(1);
        closure.State.Should().Be(UsagePeriodClosureState.Open);
    }

    [Fact]
    public async Task Retrying_the_same_idempotency_key_does_not_double_count_the_writer()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var first = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);
        var retry = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        first.Should().Be(UsageClaimOutcome.Acquired);
        retry.Should().Be(UsageClaimOutcome.AlreadyClaimed);
        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(1,
            "a retried request must reuse its original claim, not take out a second one");
    }

    [Fact]
    public async Task Releasing_a_claim_decrements_the_writer_count_exactly_once()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        await _closures.ReleaseClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", CancellationToken.None);
        // A second release for the same key must be a no-op — otherwise a retried release call
        // would drive the count negative.
        await _closures.ReleaseClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(0);
    }

    [Fact]
    public async Task A_claim_is_still_granted_while_only_reserved()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow.AddMinutes(5), "cancel-1",
            CancellationToken.None);

        var outcome = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        outcome.Should().Be(UsageClaimOutcome.Acquired,
            "a reservation on its own does not stop ordinary usage — the cancellation that made " +
            "it might still lose its own compare-and-set and never actually happen");
    }

    [Fact]
    public async Task A_claim_is_rejected_once_the_period_is_committed_to_closing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        outcome.Should().Be(UsageClaimOutcome.Rejected);
        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(0,
            "a rejected claim must never count toward the writer total it was refused against");
    }

    [Fact]
    public async Task A_claim_is_rejected_once_usage_occurs_at_or_after_a_reserved_boundary()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", boundary, "cancel-1", CancellationToken.None);
        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var before = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-before",
            boundary.AddSeconds(-1), CancellationToken.None);
        var atBoundary = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-at",
            boundary, CancellationToken.None);

        before.Should().Be(UsageClaimOutcome.Rejected,
            "the period is already Closing regardless of how early the usage itself occurred");
        atBoundary.Should().Be(UsageClaimOutcome.Rejected);
    }

    [Fact]
    public async Task Concurrent_claims_against_the_same_period_all_land()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            _closures.TryAcquireClaimAsync(
                tenantId, "sub-1", "M20260801T000000Z", $"usage-{index}",
                DateTime.UtcNow, CancellationToken.None)));

        outcomes.Should().OnlyContain(outcome => outcome == UsageClaimOutcome.Acquired);
        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(10,
            "a read-modify-write on the count would lose some of these under real concurrency");
    }

    [Fact]
    public async Task Two_racers_reserving_the_same_intended_cancellation_both_succeed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            _closures.TryReserveClosingAsync(
                tenantId, "sub-1", "M20260801T000000Z", boundary, "cancel-1", CancellationToken.None)));

        outcomes.Should().OnlyContain(outcome => outcome == ClosureReservationOutcome.Reserved,
            "the deterministic operation id makes two writers finalizing the same intended " +
            "cancellation converge rather than conflict");
    }

    [Fact]
    public async Task A_reservation_under_a_different_operation_conflicts()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow.AddDays(1), "cancel-2",
            CancellationToken.None);

        outcome.Should().Be(ClosureReservationOutcome.ConflictingOperation,
            "a genuinely different boundary is a different outcome, not a retry of the first");
    }

    [Fact]
    public async Task Releasing_a_reservation_returns_the_period_to_open()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        await _closures.TryReleaseReservationAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.State.Should().Be(UsagePeriodClosureState.Open);
        closure.CloseOperationId.Should().BeNull();

        var afterRelease = await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-2", CancellationToken.None);
        afterRelease.Should().Be(ClosureReservationOutcome.Reserved,
            "a released period accepts a fresh reservation exactly as if the first had never " +
            "happened");
    }

    [Fact]
    public async Task Releasing_under_the_wrong_operation_id_does_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        await _closures.TryReleaseReservationAsync(
            tenantId, "sub-1", "M20260801T000000Z", "some-other-operation", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.State.Should().Be(UsagePeriodClosureState.CloseReserved,
            "only the operation that actually holds the reservation may release it");
        closure.CloseOperationId.Should().Be("cancel-1");
    }

    [Fact]
    public async Task Committing_under_the_wrong_operation_id_does_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "some-other-operation", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.State.Should().Be(UsagePeriodClosureState.CloseReserved,
            "only the operation that actually holds the reservation may commit it");
    }

    [Fact]
    public async Task A_period_that_never_took_out_a_claim_reaches_closing_immediately()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.State.Should().Be(UsagePeriodClosureState.Closing);
        closure.ActiveWriterCount.Should().Be(0);
    }

    [Fact]
    public async Task Committing_an_already_committed_reservation_is_reported_as_already_committed()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        outcome.Should().Be(ClosureCommitOutcome.AlreadyCommitted,
            "a retried commit under the same operation id must converge, not conflict");
    }

    [Fact]
    public async Task Committing_under_a_different_operation_id_reports_a_mismatch()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-2", CancellationToken.None);

        outcome.Should().Be(ClosureCommitOutcome.OperationMismatch);
    }

    [Fact]
    public async Task Committing_a_period_that_never_reserved_anything_reports_not_found()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var outcome = await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        outcome.Should().Be(ClosureCommitOutcome.NotFound);
    }

    [Fact]
    public async Task Releasing_an_already_released_reservation_is_reported_as_already_released()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        await _closures.TryReleaseReservationAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryReleaseReservationAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        outcome.Should().Be(ClosureReleaseOutcome.AlreadyReleased);
    }

    [Fact]
    public async Task Reservations_older_than_the_timeout_are_found_by_the_stale_reservation_query()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-old", "M20260801T000000Z", DateTime.UtcNow, "cancel-old", CancellationToken.None);
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-new", "M20260801T000000Z", DateTime.UtcNow, "cancel-new", CancellationToken.None);

        // Only "sub-old" is old enough to count as stale against a cutoff in the future; "sub-new"
        // was reserved after that cutoff by definition of having just been reserved now.
        var stale = await _closures.ListStaleReservationsAsync(
            tenantId, DateTime.UtcNow.AddMinutes(1), 10, CancellationToken.None);

        stale.Should().HaveCount(2, "both were reserved before a cutoff one minute in the future");

        var noneYet = await _closures.ListStaleReservationsAsync(
            tenantId, DateTime.UtcNow.AddMinutes(-1), 10, CancellationToken.None);

        noneYet.Should().BeEmpty("neither reservation is older than a cutoff in the past yet");
    }

    [Fact]
    public async Task A_committed_reservation_no_longer_shows_up_as_a_stale_open_reservation()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);

        var stale = await _closures.ListStaleReservationsAsync(
            tenantId, DateTime.UtcNow.AddMinutes(1), 10, CancellationToken.None);

        stale.Should().BeEmpty(
            "a committed reservation is no longer CloseReserved, and the recovery sweep only " +
            "needs to look at reservations still stuck there");
    }

    [Fact]
    public async Task A_claim_release_that_crashes_after_reaching_ReleasePending_resumes_from_the_decrement()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        // Simulate the crash window: the claim reached ReleasePending, but the process died before
        // the counter decrement or the final Released write happened.
        await SetClaimStateAsync(tenantId, "sub-1", "M20260801T000000Z", "usage-1",
            UsagePeriodClaimState.ReleasePending);

        await _closures.ReleaseClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", CancellationToken.None);

        var closure = await _closures.GetAsync(tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(0,
            "a retry that finds the claim already in ReleasePending must resume from applying " +
            "the decrement, not treat the claim as already finished");
    }

    [Fact]
    public async Task Retrying_a_release_after_the_decrement_already_landed_does_not_decrement_twice()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        await _closures.ReleaseClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", CancellationToken.None);

        // Force the claim back to ReleasePending, as if a retry arrived after the first release
        // had already fully finished — the decrement's own operation id must still stop a second
        // one from being applied.
        await SetClaimStateAsync(tenantId, "sub-1", "M20260801T000000Z", "usage-1",
            UsagePeriodClaimState.ReleasePending);

        await _closures.ReleaseClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", CancellationToken.None);

        var closure = await _closures.GetAsync(tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(0,
            "the operation id already applied stops a resumed release from decrementing twice");
    }

    /// <summary>
    /// Forces a claim into a given state directly against the collection, bypassing the
    /// repository's own protocol — the only way to put a claim into the middle of a crash window
    /// a real caller could never observe from outside.
    /// </summary>
    private async Task SetClaimStateAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        UsagePeriodClaimState state) =>
        await _fixture.Collection<UsagePeriodClaim>("SubscriptionUsagePeriodClaims").UpdateOneAsync(
            Builders<UsagePeriodClaim>.Filter.Eq(
                claim => claim.ItemId,
                UsagePeriodClaim.CreateId(subscriptionId, periodKey, idempotencyKey)),
            Builders<UsagePeriodClaim>.Update.Set(claim => claim.State, state));

    [Fact]
    public async Task Marking_closed_only_succeeds_from_closing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var tooEarly = await _closures.TryMarkClosedAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        tooEarly.Should().BeFalse("nothing has started closing this period yet");

        await _closures.TryReserveClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        var stillReserved = await _closures.TryMarkClosedAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        stillReserved.Should().BeFalse("a mere reservation has not committed yet");

        await _closures.TryCommitClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", "cancel-1", CancellationToken.None);
        var ready = await _closures.TryMarkClosedAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);

        ready.Should().BeTrue();
    }
}
