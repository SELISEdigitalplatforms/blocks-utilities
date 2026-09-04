using FluentAssertions;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The tenant-usage-analytics rollup collections, against a real MongoDB.
/// </summary>
/// <remarks>
/// What matters here is a property of the Mongo write itself, not of C#: the idempotent upsert
/// that lets a re-run over an already-applied page fold nothing twice. A mocked repository cannot
/// observe that — it would have to reimplement the same conditional-filter logic to fake it, which
/// proves nothing about whether the real filter and the real duplicate-key race actually behave
/// this way against a server. Mirrors <see cref="SubscriptionUsageCurrentIntegrationTests"/>'s own
/// conventions.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionUsageActivityRollupIntegrationTests
{
    private readonly SubscriptionUsageActivityRollupRepository _activity;
    private readonly SubscriptionUsageActorRollupRepository _actors;

    public SubscriptionUsageActivityRollupIntegrationTests(MongoIntegrationFixture fixture)
    {
        _activity = new SubscriptionUsageActivityRollupRepository(fixture.DbContextProvider);
        _actors = new SubscriptionUsageActorRollupRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task Applying_one_entry_creates_a_bucket_with_its_figures()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var day = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        await _activity.ApplyAsync(
            tenantId, "org-1", "sub-1", "screening", "plan-1", "pro",
            day, hourUtc: 14, delta: 5m,
            recordedAtUtc: day.AddHours(14).AddMinutes(1), sourceRecordId: "rec-1",
            updatedAtUtc: DateTime.UtcNow, CancellationToken.None);

        var page = await _activity.ListAsync(
            tenantId, "org-1", "sub-1", "screening", null, null, 10, null, CancellationToken.None);

        page.Items.Should().ContainSingle();
        var bucket = page.Items[0];
        bucket.ConsumedQuantity.Should().Be(5m);
        bucket.EntryCount.Should().Be(1);
        bucket.HourlyQuantity[14].Should().Be(1);
        bucket.PlanId.Should().Be("plan-1");
        bucket.PlanCode.Should().Be("pro");
    }

    [Fact]
    public async Task Two_entries_the_same_day_accumulate_into_one_bucket()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var day = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc);

        await _activity.ApplyAsync(
            tenantId, "org-1", "sub-1", "screening", "plan-1", "pro",
            day, hourUtc: 8, delta: 3m,
            recordedAtUtc: day.AddHours(8), sourceRecordId: "rec-1",
            updatedAtUtc: DateTime.UtcNow, CancellationToken.None);
        await _activity.ApplyAsync(
            tenantId, "org-1", "sub-1", "screening", "plan-1", "pro",
            day, hourUtc: 20, delta: 4m,
            recordedAtUtc: day.AddHours(20), sourceRecordId: "rec-2",
            updatedAtUtc: DateTime.UtcNow, CancellationToken.None);

        var page = await _activity.ListAsync(
            tenantId, "org-1", "sub-1", "screening", null, null, 10, null, CancellationToken.None);

        var bucket = page.Items.Should().ContainSingle().Subject;
        bucket.ConsumedQuantity.Should().Be(7m);
        bucket.EntryCount.Should().Be(2);
        bucket.HourlyQuantity[8].Should().Be(1);
        bucket.HourlyQuantity[20].Should().Be(1);
    }

    /// <summary>
    /// Re-running the same entry must not double-count it — the guarantee the per-bucket
    /// <c>SourceCursor</c> comparison exists for, proven here against a real duplicate-key race
    /// rather than a mock that would just do whatever the test told it to.
    /// </summary>
    [Fact]
    public async Task Re_applying_the_same_entry_does_not_double_count()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var day = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);
        var recordedAt = day.AddHours(9);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await _activity.ApplyAsync(
                tenantId, "org-1", "sub-1", "screening", "plan-1", "pro",
                day, hourUtc: 9, delta: 10m,
                recordedAtUtc: recordedAt, sourceRecordId: "rec-1",
                updatedAtUtc: DateTime.UtcNow, CancellationToken.None);
        }

        var page = await _activity.ListAsync(
            tenantId, "org-1", "sub-1", "screening", null, null, 10, null, CancellationToken.None);

        var bucket = page.Items.Should().ContainSingle().Subject;
        bucket.ConsumedQuantity.Should().Be(10m, "the second application repeats a record already folded in");
        bucket.EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task A_reversal_nets_the_actor_bucket_back_to_zero()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var day = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        await _actors.ApplyAsync(
            tenantId, "org-1", "screening", day, "user-a", delta: 8m,
            recordedAtUtc: day.AddHours(1), sourceRecordId: "rec-1",
            updatedAtUtc: DateTime.UtcNow, CancellationToken.None);
        await _actors.ApplyAsync(
            tenantId, "org-1", "screening", day, "user-a", delta: -8m,
            recordedAtUtc: day.AddHours(2), sourceRecordId: "rec-1:reversal",
            updatedAtUtc: DateTime.UtcNow, CancellationToken.None);

        var page = await _actors.ListAsync(
            tenantId, "org-1", "screening", null, null, 10, null, CancellationToken.None);

        var bucket = page.Items.Should().ContainSingle().Subject;
        bucket.ConsumedQuantity.Should().Be(0m);
        bucket.EntryCount.Should().Be(2);
    }
}
