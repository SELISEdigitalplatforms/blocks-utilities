using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// Fractional quantities against a real MongoDB.
/// </summary>
/// <remarks>
/// Every claim this change rests on is a property of a Mongo write rather than of C#, and none of
/// them can be observed against a mock:
/// <list type="bullet">
/// <item><c>$inc</c> with a <c>Decimal128</c> onto a field that currently holds an <c>Int64</c>
/// promotes the field in place. This is the whole reason the change needs no data migration — every
/// counter and ledger row already written holds <c>NumberLong</c>. If it were false, the append-only
/// usage ledger would have to be rewritten, and that ledger is the authority every past invoice was
/// computed from;</item>
/// <item>a fractional balance survives the round trip through the atomic counter exactly, so a
/// reversal cancels what it compensates;</item>
/// <item>the projection's <c>$max</c> and <c>$subtract</c> derive fractional balances rather than
/// truncating them to whole units.</item>
/// </list>
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class FractionalUsageQuantityIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionUsageRepository _usage;
    private readonly SubscriptionUsageCurrentRepository _current;

    public FractionalUsageQuantityIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _usage = new SubscriptionUsageRepository(fixture.DbContextProvider);
        _current = new SubscriptionUsageCurrentRepository(fixture.DbContextProvider);
    }

    /// <summary>
    /// A counter written before quantities were fractional accepts a fractional increment, and the
    /// field becomes a decimal in place.
    /// </summary>
    /// <remarks>
    /// The migration guarantee, at the database rather than at the serializer. The document is
    /// planted as <c>NumberLong</c> by hand, exactly as every counter in every tenant database holds
    /// it today, and then incremented by a fraction through the ordinary code path.
    /// </remarks>
    [Fact]
    public async Task A_whole_number_balance_accepts_a_fractional_increment()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var counterId = SubscriptionUsageCounter.CreateId(Sub(tenantId), "storage", "M2026-09");

        // Planted the way a build before this change wrote it.
        await Raw(tenantId).InsertOneAsync(new BsonDocument
        {
            ["_id"] = counterId,
            ["TenantId"] = tenantId,
            ["OrganizationId"] = "org-1",
            ["SubscriptionId"] = Sub(tenantId),
            ["MeterKey"] = "storage",
            ["PeriodKey"] = "M2026-09",
            ["Balance"] = new BsonInt64(400),
            ["AppliedRecordCount"] = new BsonInt64(4),
            ["LimitSnapshot"] = new BsonInt64(500),
            ["NotifiedThresholds"] = new BsonArray(),
            ["PeriodStartUtc"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["PeriodEndUtc"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ["ExpiresAtUtc"] = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ["LastUpdatedAtUtc"] = DateTime.UtcNow
        });

        var counter = await _usage.ApplyDeltaAsync(
            Seed(tenantId, counterId),
            0.5m,
            CancellationToken.None);

        counter.Balance.Should().Be(400.5m);
        counter.AppliedRecordCount.Should().Be(5);

        var stored = await Raw(tenantId)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", counterId))
            .SingleAsync();

        stored["Balance"].BsonType.Should().Be(
            BsonType.Decimal128,
            "the field has to promote in place, or the next read would truncate it");
        stored["Balance"].AsDecimal.Should().Be(400.5m);
        stored["LimitSnapshot"].BsonType.Should().Be(
            BsonType.Int64,
            "a field nothing wrote is left exactly as it was, and still reads back as a decimal");
    }

    /// <summary>
    /// A run of fractional increments and one reversal of their total leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The reason quantities are exact decimals rather than doubles. A binary residue here would be
    /// carried in the counter for the rest of the period and billed as overage — and it would be
    /// stored, so no later correction could tell it from real usage.
    /// </remarks>
    [Fact]
    public async Task Fractional_increments_and_their_reversal_cancel_exactly()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var counterId = SubscriptionUsageCounter.CreateId(Sub(tenantId), "storage", "M2026-09");

        foreach (var _ in Enumerable.Range(0, 3))
        {
            await _usage.ApplyDeltaAsync(
                Seed(tenantId, counterId), 0.333333m, CancellationToken.None);
        }

        var reversed = await _usage.ApplyDeltaAsync(
            Seed(tenantId, counterId), -0.999999m, CancellationToken.None);

        reversed.Balance.Should().Be(0m);
    }

    /// <summary>
    /// Rebuilding a counter from the ledger sums fractional deltas exactly.
    /// </summary>
    /// <remarks>
    /// The repair path. The ledger is the authority, so a sum that drifted here would have the
    /// repair sweep replace a correct counter with a wrong one.
    /// </remarks>
    [Fact]
    public async Task The_ledger_summary_sums_fractional_deltas_exactly()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        foreach (var entry in new (decimal Delta, string Key)[]
                 {
                     (0.1m, "a"), (0.2m, "b"), (0.3m, "c"), (-0.6m, "d")
                 })
        {
            (await _usage.TryAppendRecordAsync(
                new SubscriptionUsageRecord
                {
                    TenantId = tenantId,
                    OrganizationId = "org-1",
                    SubscriptionId = Sub(tenantId),
                    MeterKey = "storage",
                    PeriodKey = "M2026-09",
                    Delta = entry.Delta,
                    IdempotencyKey = entry.Key,
                    OccurredAtUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
                },
                CancellationToken.None)).Should().BeTrue();
        }

        var summary = await _usage.SummariseLedgerAsync(
            tenantId, Sub(tenantId), "storage", "M2026-09", CancellationToken.None);

        summary.RecordCount.Should().Be(4);
        summary.Balance.Should().Be(0m, "0.1 + 0.2 + 0.3 - 0.6 is exactly zero in base ten");
    }

    /// <summary>
    /// A repaired counter may hold a fraction, and the repair still refuses to run backwards.
    /// </summary>
    [Fact]
    public async Task A_repair_writes_a_fractional_balance()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var counterId = SubscriptionUsageCounter.CreateId(Sub(tenantId), "storage", "M2026-09");

        await _usage.ApplyDeltaAsync(Seed(tenantId, counterId), 1m, CancellationToken.None);

        (await _usage.TryRepairCounterAsync(
            tenantId, counterId, 12.25m, 9, CancellationToken.None)).Should().BeTrue();

        var repaired = await _usage.GetCounterAsync(tenantId, counterId, CancellationToken.None);
        repaired!.Balance.Should().Be(12.25m);

        (await _usage.TryRepairCounterAsync(
                tenantId, counterId, 999m, 9, CancellationToken.None))
            .Should()
            .BeFalse("a repair at the same record count must not overwrite a later one");
    }

    /// <summary>
    /// The projection derives fractional remaining and overage rather than truncating them.
    /// </summary>
    /// <remarks>
    /// <c>Remaining</c> and <c>Overage</c> are computed by a <c>$subtract</c> inside the merge
    /// pipeline, so they are Mongo's arithmetic and not this service's.
    /// </remarks>
    [Fact]
    public async Task The_projection_derives_a_fractional_remaining_and_overage()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _current.TryPublishAsync(
            Projection(tenantId, included: 100.5m, used: 40.25m, counterVersion: 1),
            CancellationToken.None)).Should().BeTrue();

        // A second, later publish so the merge pipeline runs rather than the plain insert.
        (await _current.TryPublishAsync(
            Projection(tenantId, included: 100.5m, used: 120.75m, counterVersion: 2),
            CancellationToken.None)).Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId,
            SubscriptionUsageCurrent.CreateId(Sub(tenantId), "storage", "M2026-09"),
            CancellationToken.None);

        stored!.Used.Should().Be(120.75m);
        stored.Remaining.Should().Be(0m);
        stored.Overage.Should().Be(20.25m);
    }

    /// <summary>
    /// The clamped side keeps the field's type rather than turning it into an integer.
    /// </summary>
    /// <remarks>
    /// The floor the derived balances clamp to is a <c>Decimal128</c> zero for exactly this reason:
    /// a long zero would leave the field holding an integer on precisely the periods that clamped,
    /// so a consumer reading this collection directly would meet two types in one field.
    /// </remarks>
    [Fact]
    public async Task A_clamped_balance_is_still_stored_as_a_decimal()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Projection(tenantId, included: 100.5m, used: 0.5m, counterVersion: 1),
            CancellationToken.None);
        await _current.TryPublishAsync(
            Projection(tenantId, included: 100.5m, used: 1.5m, counterVersion: 2),
            CancellationToken.None);

        var stored = await _fixture.Database
            .GetCollection<BsonDocument>("SubscriptionUsageCurrent")
            .Find(Builders<BsonDocument>.Filter.Eq(
                "_id",
                SubscriptionUsageCurrent.CreateId(Sub(tenantId), "storage", "M2026-09")))
            .SingleAsync();

        stored["Overage"].BsonType.Should().Be(BsonType.Decimal128);
        stored["Overage"].AsDecimal.Should().Be(0m);
        stored["Remaining"].AsDecimal.Should().Be(99m);
    }

    // Named rather than resolved through SubscriptionCollections, which is internal to the domain
    // service. The fixture maps every tenant to one database, so the collection is the same one the
    // repository writes to.
    private IMongoCollection<BsonDocument> Raw(string tenantId) =>
        _fixture.Database.GetCollection<BsonDocument>("SubscriptionUsageCounters");

    private static SubscriptionUsageCounter Seed(string tenantId, string counterId) => new()
    {
        ItemId = counterId,
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = Sub(tenantId),
        MeterKey = "storage",
        PeriodKey = "M2026-09",
        LimitSnapshot = 500m,
        PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    private static SubscriptionUsageCurrent Projection(
        string tenantId,
        decimal included,
        decimal used,
        long counterVersion) => new()
    {
        ItemId = SubscriptionUsageCurrent.CreateId(Sub(tenantId), "storage", "M2026-09"),
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = Sub(tenantId),
        SubscriptionStatus = SubscriptionStatus.Active,
        PlanId = "plan-1",
        PlanCode = "pro",
        MeterKey = "storage",
        UnitLabel = "GB",
        PeriodKey = "M2026-09",
        PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        Included = included,
        Used = used,
        Remaining = Math.Max(0, included - used),
        Overage = Math.Max(0, used - included),
        OverageAllowed = true,
        CounterVersion = counterVersion,
        SubscriptionVersion = 1,
        SchemaVersion = SubscriptionUsageCurrent.CurrentSchemaVersion,
        UpdatedAtUtc = new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    // Derived from the tenant id because the fixture maps every tenant to one database, so two
    // tests sharing a composed id would collide.
    private static string Sub(string tenantId) => $"sub-{tenantId}";
}
