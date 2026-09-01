using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The current-usage projection, against a real MongoDB.
/// </summary>
/// <remarks>
/// Every guarantee this collection makes is a property of a Mongo write, not of C#: the version
/// condition on the upsert, the uniqueness of one document per meter-period, the insert-only seed,
/// and the boundary query that picks the current window. A mocked repository proves none of them, and
/// the one that matters most — the highest version winning a race rather than the last writer —
/// cannot be observed at all without concurrent writers hitting one server.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionUsageCurrentIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionUsageCurrentRepository _current;

    public SubscriptionUsageCurrentIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _current = new SubscriptionUsageCurrentRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task Publishing_stores_every_field_a_direct_reader_depends_on()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var document = Document(tenantId, used: 40, sourceVersion: 3);

        (await _current.TryPublishAsync(document, CancellationToken.None)).Should().BeTrue();

        var stored = await _current.GetAsync(tenantId, document.ItemId, CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.OrganizationId.Should().Be("org-1");
        stored.SubscriptionId.Should().Be("sub-1");
        stored.SubscriptionStatus.Should().Be(SubscriptionStatus.Active);
        stored.PlanId.Should().Be("plan-1");
        stored.PlanCode.Should().Be("pro");
        stored.MeterKey.Should().Be("screening");
        stored.UnitLabel.Should().Be("screening");
        stored.Included.Should().Be(100);
        stored.Used.Should().Be(40);
        stored.Remaining.Should().Be(60);
        stored.Overage.Should().Be(0);
        stored.OverageAllowed.Should().BeTrue();
        stored.SourceVersion.Should().Be(3);
        stored.SchemaVersion.Should().Be(SubscriptionUsageCurrent.CurrentSchemaVersion);
    }

    [Fact]
    public async Task A_newer_version_replaces_an_older_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 10, sourceVersion: 1), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 20, sourceVersion: 2), CancellationToken.None))
            .Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(20);
        stored.SourceVersion.Should().Be(2);
    }

    /// <summary>
    /// The defect this condition exists to prevent. A request delayed between updating its counter
    /// and publishing its projection carries an older figure; without the condition it would
    /// overwrite a newer balance and leave the projection permanently behind, with nothing to say so.
    /// </summary>
    [Fact]
    public async Task An_older_version_cannot_overwrite_a_newer_one()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 90, sourceVersion: 9), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 10, sourceVersion: 4), CancellationToken.None))
            .Should().BeFalse("the stored document is already newer");

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(90, "the newer figure must survive");
        stored.SourceVersion.Should().Be(9);
    }

    /// <summary>Republishing the same version is not an update, and must not be reported as one.</summary>
    [Fact]
    public async Task Republishing_the_same_version_changes_nothing()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 7, sourceVersion: 5), CancellationToken.None);

        (await _current.TryPublishAsync(
                Document(tenantId, used: 999, sourceVersion: 5), CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(7);
    }

    /// <summary>
    /// The concurrency guarantee, with real concurrent writers.
    /// </summary>
    /// <remarks>
    /// Sixteen publishes of the same meter-period in a scrambled version order, all at once. The
    /// document must end at the highest version, and exactly one call may report having written it —
    /// the rest lost the version race, which is a success for them and a no-op for the database.
    /// Last-writer-wins would leave whichever call happened to finish last, which is the bug.
    /// </remarks>
    [Fact]
    public async Task The_highest_version_wins_a_concurrent_race_not_the_last_writer()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        // Scrambled deliberately: ascending order would pass even with no condition at all.
        var versions = new[] { 7, 3, 16, 1, 11, 5, 14, 2, 9, 6, 15, 4, 13, 8, 12, 10 };

        var results = await Task.WhenAll(versions.Select(version =>
            _current.TryPublishAsync(
                Document(tenantId, used: version * 10, sourceVersion: version),
                CancellationToken.None)));

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.SourceVersion.Should().Be(16, "the highest version must be the one that stands");
        stored.Used.Should().Be(160, "and the balance stored with it");

        results.Count(written => written)
            .Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(versions.Length);
    }

    /// <summary>
    /// One meter-period is one document, enforced by the database rather than by whoever composes the
    /// id. Two current documents for one meter would show a reader two allowances for one allowance.
    /// </summary>
    [Fact]
    public async Task Two_documents_for_one_meter_period_are_refused()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var first = Document(tenantId, used: 1, sourceVersion: 1);

        await _current.TryPublishAsync(first, CancellationToken.None);

        var duplicate = Document(tenantId, used: 2, sourceVersion: 2);
        duplicate.ItemId = "a-different-id-for-the-same-meter-period";

        var insert = async () => await _fixture.Database
            .GetCollection<SubscriptionUsageCurrent>("SubscriptionUsageCurrent")
            .InsertOneAsync(duplicate, cancellationToken: CancellationToken.None);

        await insert.Should().ThrowAsync<MongoWriteException>(
            "the unique index on subscription, meter and period must refuse it");
    }

    [Fact]
    public async Task Seeding_creates_a_zero_usage_document_when_none_exists()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        (await _current.TrySeedAsync(
                Document(tenantId, used: 0, sourceVersion: 0), CancellationToken.None))
            .Should().BeTrue();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(0);
        stored.SourceVersion.Should().Be(0);
    }

    /// <summary>
    /// A seed must never reset a live balance. If it could, a rollover or an activation replay would
    /// discard usage the customer has already been billed for.
    /// </summary>
    [Fact]
    public async Task Seeding_never_overwrites_a_published_balance()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(
            Document(tenantId, used: 250, sourceVersion: 12), CancellationToken.None);

        (await _current.TrySeedAsync(
                Document(tenantId, used: 0, sourceVersion: 0), CancellationToken.None))
            .Should().BeFalse();

        var stored = await _current.GetAsync(
            tenantId, Document(tenantId, 0, 0).ItemId, CancellationToken.None);

        stored!.Used.Should().Be(250, "the recorded usage must survive a seed");
        stored.SourceVersion.Should().Be(12);
    }

    /// <summary>
    /// Concurrent seeds of the same missing document: exactly one may create it. Otherwise a rollover
    /// running on two workers would produce two current documents for one meter.
    /// </summary>
    [Fact]
    public async Task Only_one_of_eight_concurrent_seeds_creates_the_document()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _current.TrySeedAsync(
                Document(tenantId, used: 0, sourceVersion: 0), CancellationToken.None)));

        results.Count(created => created).Should().Be(1);
    }

    /// <summary>
    /// The current window is selected by period boundary rather than by an <c>isCurrent</c> flag,
    /// because a flag has to be cleared on another document when a period rolls and there is no
    /// transaction here to make setting one and clearing the other a single act.
    /// </summary>
    [Fact]
    public async Task Only_the_window_containing_now_is_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        var previous = Document(tenantId, used: 5, sourceVersion: 1, periodKey: "M2026-08");
        previous.PeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        previous.PeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var live = Document(tenantId, used: 30, sourceVersion: 2, periodKey: "M2026-09");
        live.PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        live.PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await _current.TryPublishAsync(previous, CancellationToken.None);
        await _current.TryPublishAsync(live, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId, "org-1", "sub-1", now, CancellationToken.None);

        found.Should().ContainSingle().Which.PeriodKey.Should().Be("M2026-09");
    }

    /// <summary>
    /// A period ends the instant its successor begins. An inclusive upper bound would return both at
    /// the boundary, and a reader would see two allowances for one meter.
    /// </summary>
    [Fact]
    public async Task Exactly_one_window_is_current_at_a_period_boundary()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var boundary = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var previous = Document(tenantId, used: 5, sourceVersion: 1, periodKey: "M2026-08");
        previous.PeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        previous.PeriodEndUtc = boundary;

        var next = Document(tenantId, used: 0, sourceVersion: 1, periodKey: "M2026-09");
        next.PeriodStartUtc = boundary;
        next.PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await _current.TryPublishAsync(previous, CancellationToken.None);
        await _current.TryPublishAsync(next, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId, "org-1", "sub-1", boundary, CancellationToken.None);

        found.Should().ContainSingle().Which.PeriodKey.Should().Be("M2026-09");
    }

    /// <summary>
    /// A lifetime capacity meter's window runs to <c>DateTime.MaxValue</c>, so the same boundary query
    /// has to select it without naming it as a special case.
    /// </summary>
    [Fact]
    public async Task A_lifetime_window_is_always_current()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var lifetime = Document(tenantId, used: 3, sourceVersion: 1, periodKey: "LIFETIME");
        lifetime.MeterKey = "storage";
        lifetime.ItemId = SubscriptionUsageCurrent.CreateId("sub-1", "storage", "LIFETIME");
        lifetime.PeriodStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        lifetime.PeriodEndUtc = DateTime.MaxValue;

        await _current.TryPublishAsync(lifetime, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId,
            "org-1",
            "sub-1",
            new DateTime(2031, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().ContainSingle().Which.MeterKey.Should().Be("storage");
    }

    /// <summary>
    /// Organization scope is in the filter, not merely in the caller's intent. Without it, a direct
    /// read would be a cross-organization read of billing state.
    /// </summary>
    [Fact]
    public async Task Another_organizations_document_is_never_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        var theirs = Document(tenantId, used: 42, sourceVersion: 1);
        theirs.OrganizationId = "org-2";
        theirs.SubscriptionId = "sub-2";
        theirs.ItemId = SubscriptionUsageCurrent.CreateId("sub-2", "screening", "M2026-09");

        await _current.TryPublishAsync(Document(tenantId, 5, 1), CancellationToken.None);
        await _current.TryPublishAsync(theirs, CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            tenantId,
            "org-1",
            "sub-1",
            new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().ContainSingle().Which.OrganizationId.Should().Be("org-1");
    }

    /// <summary>
    /// A tenant selects the database, so one tenant's projection must be unreachable from another's
    /// even with the same organization and subscription ids.
    /// </summary>
    [Fact]
    public async Task Another_tenants_document_is_never_returned()
    {
        var mine = MongoIntegrationFixture.NewTenantId();
        var theirs = MongoIntegrationFixture.NewTenantId();

        await _current.TryPublishAsync(Document(theirs, used: 77, sourceVersion: 1), CancellationToken.None);

        var found = await _current.ListCurrentAsync(
            mine,
            "org-1",
            "sub-1",
            new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        found.Should().BeEmpty();
        (await _current.GetAsync(mine, Document(mine, 0, 0).ItemId, CancellationToken.None))
            .Should().BeNull();
    }

    /// <summary>
    /// The reconciliation pass reads oldest-updated first, because a projection that has not been
    /// rewritten for a while is the one most likely to be behind its counter.
    /// </summary>
    [Fact]
    public async Task Reconciliation_candidates_come_back_oldest_first_and_bounded()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        foreach (var (meter, minutesAgo) in new[] { ("a", 5), ("b", 60), ("c", 1) })
        {
            var document = Document(tenantId, used: 1, sourceVersion: 1);
            document.MeterKey = meter;
            document.ItemId = SubscriptionUsageCurrent.CreateId("sub-1", meter, "M2026-09");
            document.UpdatedAtUtc = now.AddMinutes(-minutesAgo);

            await _current.TryPublishAsync(document, CancellationToken.None);
        }

        var candidates = await _current.ListBehindCountersAsync(
            tenantId, now, 2, CancellationToken.None);

        candidates.Should().HaveCount(2, "the pass is bounded");
        candidates[0].MeterKey.Should().Be("b", "the least recently written comes first");
        candidates[1].MeterKey.Should().Be("a");
    }

    /// <summary>
    /// The indexes are what make a direct read safe to expose: without them a consumer could express
    /// a query that scans the collection. Asserted on the stored index list rather than on the
    /// builder code, because only the former is what the database will actually use.
    /// </summary>
    [Fact]
    public async Task The_declared_indexes_exist_on_the_collection()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await _current.EnsureIndexesAsync(tenantId, CancellationToken.None);

        // The tenant selects the database, not a collection-name prefix: SubscriptionCollections.Of
        // resolves a database per tenant and then a plainly named collection inside it.
        var indexes = await _fixture.Database
            .GetCollection<BsonDocument>("SubscriptionUsageCurrent")
            .Indexes.List()
            .ToListAsync();

        var names = indexes.ConvertAll(index => index["name"].AsString);

        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentUniqueIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentReadIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentStalenessIndexName);
        names.Should().Contain(SubscriptionIndexDefinitions.UsageCurrentExpiryIndexName);

        indexes
            .Should().ContainSingle(index =>
                index["name"].AsString == SubscriptionIndexDefinitions.UsageCurrentUniqueIndexName)
            .Which["unique"].AsBoolean.Should().BeTrue();

        indexes
            .Should().ContainSingle(index =>
                index["name"].AsString == SubscriptionIndexDefinitions.UsageCurrentExpiryIndexName)
            .Which.Contains("expireAfterSeconds").Should().BeTrue(
                "the projection must not outlive the counter it projects");
    }

    private static SubscriptionUsageCurrent Document(
        string tenantId,
        long used,
        long sourceVersion,
        string periodKey = "M2026-09") => new()
    {
        ItemId = SubscriptionUsageCurrent.CreateId("sub-1", "screening", periodKey),
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = "sub-1",
        SubscriptionStatus = SubscriptionStatus.Active,
        PlanId = "plan-1",
        PlanCode = "pro",
        MeterKey = "screening",
        UnitLabel = "screening",
        PeriodKey = periodKey,
        PeriodStartUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        Included = 100,
        Used = used,
        Remaining = Math.Max(0, 100 - used),
        Overage = Math.Max(0, used - 100),
        OverageAllowed = true,
        SourceVersion = sourceVersion,
        SchemaVersion = SubscriptionUsageCurrent.CurrentSchemaVersion,
        UpdatedAtUtc = new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc),
        ExpiresAtUtc = new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)
    };
}
