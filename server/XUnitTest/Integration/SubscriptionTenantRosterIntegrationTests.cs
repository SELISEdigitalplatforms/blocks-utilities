using Blocks.Genesis;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Subscription.DomainService.Repositories;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

/// <summary>
/// Which tenants the subscription repair sweep visits, against a real MongoDB.
/// </summary>
/// <remarks>
/// Guards against a tenant with subscriptions being left off the roster, which would leave its
/// unannounced renewals and activations unrepaired with nothing to report it. Needs a reachable
/// mongod, or <c>BLOCKS_IT_MONGO</c> pointing at one.
/// </remarks>
public sealed class SubscriptionTenantRosterIntegrationTests
    : IClassFixture<MongoIntegrationFixture>, IDisposable
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, IMongoDatabase> _tenantDatabases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _withoutDatabase = new(StringComparer.Ordinal);
    private readonly IMongoDatabase _root;

    public SubscriptionTenantRosterIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        // Short, unique names: Mongo caps a database name at 63 characters.
        _root = fixture.Client.GetDatabase("it_roster_" + Guid.NewGuid().ToString("N")[..12]);
    }

    public void Dispose()
    {
        _fixture.Client.DropDatabase(_root.DatabaseNamespace.DatabaseName);

        foreach (var database in _tenantDatabases.Values)
        {
            _fixture.Client.DropDatabase(database.DatabaseNamespace.DatabaseName);
        }
    }

    [Fact]
    public async Task The_backfill_rosters_only_registry_tenants_that_already_have_a_subscription()
    {
        var subscribed = await RegisterTenantAsync(hasSubscription: true);
        await RegisterTenantAsync(hasSubscription: false);
        var unprovisioned = MongoIntegrationFixture.NewTenantId();
        _withoutDatabase.Add(unprovisioned);
        await _root.GetCollection<BsonDocument>("Tenants")
            .InsertOneAsync(new BsonDocument { ["_id"] = unprovisioned, ["TenantId"] = unprovisioned });

        var tenants = await Roster().ListTenantIdsAsync(default);

        tenants.Should().Equal([subscribed],
            "a tenant that subscribed before the roster existed must still be swept, and nothing " +
            "else should be: that is the whole saving over walking the registry");
    }

    [Fact]
    public async Task The_backfill_runs_once_so_a_tenant_registered_later_is_rostered_only_by_subscribing()
    {
        await RegisterTenantAsync(hasSubscription: true);
        await Roster().ListTenantIdsAsync(default);

        var later = await RegisterTenantAsync(hasSubscription: true);
        var tenants = await Roster().ListTenantIdsAsync(default);

        tenants.Should().NotContain(later,
            "the registry walk is the cost this replaces, so a fresh process must not repeat it");

        var roster = Roster();
        await roster.RecordAsync(later, default);
        (await roster.ListTenantIdsAsync(default)).Should().Contain(later);
    }

    [Fact]
    public async Task A_failure_part_way_through_the_backfill_leaves_it_to_run_again()
    {
        var unreachable = await RegisterTenantAsync(hasSubscription: true);
        var reachable = await RegisterTenantAsync(hasSubscription: true);
        var failing = true;
        var provider = Provider(tenantId => failing && tenantId == unreachable
            ? throw new TimeoutException("tenant database briefly unreachable")
            : null);

        var first = () => Roster(provider).ListTenantIdsAsync(default);
        await first.Should().ThrowAsync<TimeoutException>();

        failing = false;
        var tenants = await Roster(provider).ListTenantIdsAsync(default);

        tenants.Should().BeEquivalentTo([unreachable, reachable],
            "skipping a tenant that was only briefly unreachable would leave it off the roster " +
            "for good, since the backfill never runs again once it completes");
    }

    [Fact]
    public async Task Recording_a_tenant_again_keeps_one_entry_and_its_first_sighting()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();

        await Roster().RecordAsync(tenantId, default);
        _time.Advance(TimeSpan.FromDays(3));
        await Roster().RecordAsync(tenantId, default);

        var entries = await _root.GetCollection<BsonDocument>("SubscriptionTenants")
            .Find(new BsonDocument("_id", tenantId))
            .ToListAsync();

        entries.Should().ContainSingle("every subscribe records its tenant, and none may add a second entry");
        entries[0]["FirstSeenAtUtc"].ToUniversalTime()
            .Should().Be(new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc));
    }

    private async Task<string> RegisterTenantAsync(bool hasSubscription)
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var database = _fixture.Client.GetDatabase("it_tenant_" + tenantId[..12]);
        _tenantDatabases[tenantId] = database;

        await _root.GetCollection<BsonDocument>("Tenants")
            .InsertOneAsync(new BsonDocument { ["_id"] = tenantId, ["TenantId"] = tenantId });

        if (hasSubscription)
        {
            await database.GetCollection<BsonDocument>("Subscriptions")
                .InsertOneAsync(new BsonDocument { ["_id"] = Guid.NewGuid().ToString(), ["TenantId"] = tenantId });
        }

        return tenantId;
    }

    private SubscriptionTenantRoster Roster(IDbContextProvider? provider = null)
    {
        var secret = new Mock<IBlocksSecret>();
        secret.SetupGet(value => value.DatabaseConnectionString)
            .Returns(MongoIntegrationFixture.ConnectionString);
        secret.SetupGet(value => value.RootDatabaseName).Returns(_root.DatabaseNamespace.DatabaseName);

        return new SubscriptionTenantRoster(provider ?? Provider(_ => null), secret.Object, _time);
    }

    /// <summary>
    /// The root by name, each registered tenant to its own database, and an unprovisioned tenant to
    /// what Genesis throws for it. <paramref name="intercept"/> may throw first.
    /// </summary>
    private IDbContextProvider Provider(Func<string, object?> intercept)
    {
        var provider = new Mock<IDbContextProvider>();
        provider
            .Setup(p => p.GetDatabase(It.IsAny<string>(), _root.DatabaseNamespace.DatabaseName))
            .Returns(_root);
        provider
            .Setup(p => p.GetDatabase(It.IsAny<string>()))
            .Returns((string tenantId) =>
            {
                intercept(tenantId);

                return _withoutDatabase.Contains(tenantId)
                    ? throw new InvalidOperationException(
                        "Could not initialize database for tenant",
                        new KeyNotFoundException("Database information is missing for tenant"))
                    : _tenantDatabases[tenantId];
            });

        return provider.Object;
    }
}
