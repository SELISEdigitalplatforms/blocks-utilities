using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// Re-arming usage alerts after a credit purchase, against a real MongoDB.
/// </summary>
/// <remarks>
/// Guards against an alert a subscriber was already sent being sent again. Re-arming reads the
/// counter, decides which thresholds the new credits moved out of reach, and writes; a recording
/// can mark a threshold in between. Only the database can show that the write removes the named
/// thresholds and leaves the one just marked alone.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class ThresholdRearmIntegrationTests
{
    private readonly SubscriptionUsageRepository _usage;

    public ThresholdRearmIntegrationTests(MongoIntegrationFixture fixture) =>
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);

    [Fact]
    public async Task Clearing_a_rearmed_threshold_keeps_one_a_concurrent_recording_just_marked()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var counterId = SubscriptionUsageCounter.CreateId($"sub-{tenantId}", "ai-credit", "M2026-10");

        await _usage.ApplyDeltaAsync(Seed(tenantId, counterId), 95m, CancellationToken.None);
        await _usage.TryMarkThresholdNotifiedAsync(tenantId, counterId, 80, CancellationToken.None);

        // Re-arming has read [80] and decided to clear it; a recording marks 90 before it writes.
        await _usage.TryMarkThresholdNotifiedAsync(tenantId, counterId, 90, CancellationToken.None);
        var cleared = await _usage.TryClearThresholdsAsync(tenantId, counterId, [80], CancellationToken.None);

        var counter = await _usage.GetCounterAsync(tenantId, counterId, CancellationToken.None);
        cleared.Should().BeTrue();
        counter!.NotifiedThresholds.Should().Equal([90],
            "80 may fire again now credits moved it out of reach, but 90 was already sent and must not be sent twice");
    }

    private static SubscriptionUsageCounter Seed(string tenantId, string counterId) => new()
    {
        ItemId = counterId,
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = $"sub-{tenantId}",
        MeterKey = "ai-credit",
        PeriodKey = "M2026-10",
        LimitSnapshot = 100m,
        PeriodStartUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };
}
