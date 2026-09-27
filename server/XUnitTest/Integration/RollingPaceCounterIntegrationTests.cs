using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The rolling pace counter's atomic write, against a real MongoDB.
/// </summary>
/// <remarks>
/// A single mock in <c>UsageSubLimitTests</c> can only prove the arithmetic is right; it cannot
/// prove the write is atomic, because a mock has no concurrency to get wrong. Two properties here
/// depend specifically on Mongo's own guarantees and cannot be observed any other way:
/// <list type="bullet">
/// <item>two increments to the same minute's bucket, issued concurrently, both land — <c>$inc</c>
/// on a dotted path serialises the same way <c>$inc</c> on a top-level field does, which is the
/// whole reason a rolling limit can still be an enforcement point rather than a check two callers
/// could both pass;</item>
/// <item>an increment to one bucket and an <c>Unset</c> pruning a different, stale bucket in the
/// same write leave the fresh bucket untouched — a single combined update, not two.</item>
/// </list>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class RollingPaceCounterIntegrationTests
{
    private readonly SubscriptionUsageRepository _usage;

    public RollingPaceCounterIntegrationTests(MongoIntegrationFixture fixture)
    {
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task Concurrent_increments_to_the_same_bucket_both_land()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = Seed(tenantId);

        var writes = Enumerable.Range(0, 20)
            .Select(_ => _usage.ApplyBucketDeltaAsync(
                seed, "202608140900", 1, [], CancellationToken.None));

        await Task.WhenAll(writes);

        var stored = await _usage.GetCounterAsync(tenantId, seed.ItemId, CancellationToken.None);

        stored!.Buckets!["202608140900"].Should().Be(20,
            because: "a lost update here is a use nobody was refused for, and the whole point of " +
                     "bucketing rather than summing the ledger is that this cannot happen");
    }

    [Fact]
    public async Task Pruning_a_stale_bucket_does_not_touch_the_one_being_written()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var seed = Seed(tenantId);

        await _usage.ApplyBucketDeltaAsync(
            seed, "202608140500", 9, [], CancellationToken.None);

        var current = await _usage.ApplyBucketDeltaAsync(
            seed, "202608141000", 1, ["202608140500"], CancellationToken.None);

        current.Buckets.Should().ContainSingle().Which.Key.Should().Be("202608141000",
            because: "the increment and the prune are one write, and a prune that raced ahead of " +
                     "its own increment would either drop the fresh bucket or leave the stale one " +
                     "behind depending on which half landed first");
    }

    /// <remarks>
    /// The tenant is folded into the subscription id, not just carried in <see
    /// cref="SubscriptionUsageCounter.TenantId"/>: the fixture maps every tenant onto one
    /// physical database, so the tenant field alone does not keep two tests' documents apart —
    /// only the counter's own <c>ItemId</c>, which is derived from the subscription id, does.
    /// Without this, two tests in this file collided on the identical counter document.
    /// </remarks>
    private static SubscriptionUsageCounter Seed(string tenantId)
    {
        var subscriptionId = $"sub-{tenantId}";

        return new SubscriptionUsageCounter
        {
            ItemId = SubscriptionUsageCounter.CreateId(subscriptionId, "ai_tokens", "r5h"),
            TenantId = tenantId,
            OrganizationId = "org-1",
            SubscriptionId = subscriptionId,
            MeterKey = "ai_tokens",
            PeriodKey = "r5h",
            LimitSnapshot = 10,
            PeriodStartUtc = new DateTime(2026, 8, 14, 5, 0, 0, DateTimeKind.Utc),
            PeriodEndUtc = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc),
            ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
        };
    }
}
