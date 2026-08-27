using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The atomicity a usage claim actually needs, which only a real database can demonstrate: a
/// mocked repository would answer with whatever a test told it to, proving nothing about whether
/// two concurrent claims against the same period can really only produce one active writer count,
/// or whether a claim's own idempotency key really is enforced by something other than good faith.
/// </summary>
[Collection(MongoIntegrationCollection.Name)]
public sealed class UsagePeriodClosureRepositoryIntegrationTests
{
    private readonly UsagePeriodClosureRepository _closures;

    public UsagePeriodClosureRepositoryIntegrationTests(MongoIntegrationFixture fixture) =>
        _closures = new UsagePeriodClosureRepository(fixture.DbContextProvider);

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
    public async Task A_claim_is_rejected_once_the_period_is_closing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        var outcome = await _closures.TryAcquireClaimAsync(
            tenantId, "sub-1", "M20260801T000000Z", "usage-1", DateTime.UtcNow, CancellationToken.None);

        outcome.Should().Be(UsageClaimOutcome.Rejected);
        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.ActiveWriterCount.Should().Be(0,
            "a rejected claim must never count toward the writer total it was refused against");
    }

    [Fact]
    public async Task A_claim_is_rejected_once_usage_occurs_at_or_after_the_boundary()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", boundary, "cancel-1", CancellationToken.None);

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
    public async Task Starting_to_close_is_idempotent_and_keeps_the_first_boundary()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var firstBoundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", firstBoundary, "cancel-1", CancellationToken.None);
        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z",
            firstBoundary.AddDays(1), "cancel-2", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.EffectiveEndUtc.Should().Be(firstBoundary,
            "only one cancellation ever finalizes a given period; a second call must not move " +
            "the boundary that was already recorded");
    }

    [Fact]
    public async Task Closing_a_period_that_never_took_out_a_claim_is_immediately_ready_to_rate()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);

        var closure = await _closures.GetAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        closure!.State.Should().Be(UsagePeriodClosureState.Closing);
        closure.ActiveWriterCount.Should().Be(0);
    }

    [Fact]
    public async Task Marking_closed_only_succeeds_from_closing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var tooEarly = await _closures.TryMarkClosedAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);
        tooEarly.Should().BeFalse("nothing has started closing this period yet");

        await _closures.StartClosingAsync(
            tenantId, "sub-1", "M20260801T000000Z", DateTime.UtcNow, "cancel-1", CancellationToken.None);
        var ready = await _closures.TryMarkClosedAsync(
            tenantId, "sub-1", "M20260801T000000Z", CancellationToken.None);

        ready.Should().BeTrue();
    }
}
