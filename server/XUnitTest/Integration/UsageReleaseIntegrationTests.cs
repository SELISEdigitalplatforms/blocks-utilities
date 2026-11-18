using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The two reads a usage release depends on: which grants a booking drew from, and whether the
/// booking is still being written.
/// </summary>
[Collection(MongoIntegrationCollection.Name)]
public sealed class UsageReleaseIntegrationTests
{
    private const string Period = "M20260801T000000Z";

    private readonly SubscriptionUsageRepository _usage;
    private readonly UsagePeriodClosureRepository _closures;

    public UsageReleaseIntegrationTests(MongoIntegrationFixture fixture)
    {
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);
        _closures = new UsagePeriodClosureRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_bookings_credit_draw_is_saved_and_read_back_with_it()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var record = new SubscriptionUsageRecord
        {
            TenantId = tenantId,
            SubscriptionId = "sub-1",
            MeterKey = "ai-credit",
            PeriodKey = Period,
            Delta = 10,
            IdempotencyKey = "booking-1"
        };
        await _usage.TryAppendRecordAsync(record, CancellationToken.None);

        await _usage.SetCreditTakesAsync(
            tenantId,
            record.ItemId,
            [
                new UsageCreditTake { GrantId = "g1", SubscriptionId = "sub-1", Amount = 3.5m },
                new UsageCreditTake { GrantId = "g2", Amount = 1.5m }
            ],
            CancellationToken.None);

        var stored = await _usage.FindRecordByKeyAsync(tenantId, "sub-1", "booking-1", CancellationToken.None);

        stored!.CreditTakes.Should().BeEquivalentTo(
        [
            new UsageCreditTake { GrantId = "g1", SubscriptionId = "sub-1", Amount = 3.5m },
            new UsageCreditTake { GrantId = "g2", SubscriptionId = null, Amount = 1.5m }
        ]);
        stored.Delta.Should().Be(10, "only the draw is set after insert; the entry itself never changes");
    }

    [Fact]
    public async Task A_booking_is_active_only_until_its_claim_is_released()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscriptionId = "sub-" + tenantId;

        (await _closures.IsClaimActiveAsync(tenantId, subscriptionId, Period, "booking-1", CancellationToken.None))
            .Should().BeFalse("no booking was ever made under this key");

        await _closures.TryAcquireClaimAsync(
            tenantId, subscriptionId, Period, "booking-1", DateTime.UtcNow, CancellationToken.None);

        (await _closures.IsClaimActiveAsync(tenantId, subscriptionId, Period, "booking-1", CancellationToken.None))
            .Should().BeTrue();
        (await _closures.IsClaimActiveAsync(tenantId, subscriptionId, Period, "booking-2", CancellationToken.None))
            .Should().BeFalse("only this one key's write is in flight");

        await _closures.ReleaseClaimAsync(tenantId, subscriptionId, Period, "booking-1", CancellationToken.None);

        (await _closures.IsClaimActiveAsync(tenantId, subscriptionId, Period, "booking-1", CancellationToken.None))
            .Should().BeFalse("a released claim means every write of the booking has landed");
    }
}
