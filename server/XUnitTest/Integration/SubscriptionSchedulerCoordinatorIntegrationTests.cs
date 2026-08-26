using Blocks.Genesis;
using FluentAssertions;
using MongoDB.Driver;
using Moq;
using Subscription.DomainService.Scheduling;
using XUnitTest.Payment;

namespace XUnitTest.Integration;

/// <summary>
/// The coordination record's guarantees, against a real MongoDB.
/// </summary>
/// <remarks>
/// Three of them are properties of the database rather than of any class, which is why they cannot
/// be shown with a fake: a single record whichever replica writes first, a generation that advances
/// once however many replicas propose the same change, and a heartbeat stamped by the database so
/// that liveness does not depend on every pod's clock agreeing.
/// <para>
/// Needs a reachable mongod, or <c>BLOCKS_IT_MONGO</c> pointing at one.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerCoordinatorIntegrationTests
    : IClassFixture<MongoIntegrationFixture>
{
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(15);

    private readonly MongoIntegrationFixture _fixture;

    public SubscriptionSchedulerCoordinatorIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;

        // Emptied per test: the record is a single fixed key and the roster is keyed by worker name,
        // so without this one test's fleet is the next test's fleet. The class fixture shares one
        // database across every test in this file.
        Modes().DeleteMany(FilterDefinition<SubscriptionSchedulerModeRecord>.Empty);
        Replicas().DeleteMany(FilterDefinition<SubscriptionSchedulerReplica>.Empty);
    }

    [Fact]
    public async Task A_fleet_starting_at_once_ends_up_with_one_record()
    {
        var coordinator = Coordinator();

        // Concurrently rather than in sequence: sequential calls would pass even if seeding were a
        // read followed by a write, which is the bug this is here to exclude.
        var races = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index =>
                coordinator.TrySeedAsync(SchedulerRunMode.Direct, $"worker-{index}", default)));

        races.Count(seeded => seeded).Should().Be(1);

        var view = await coordinator.ReadFleetAsync(Generous, default);
        view.Record!.Generation.Should().Be(1);
        view.Record.DesiredMode.Should().Be(SchedulerRunMode.Direct);
    }

    [Fact]
    public async Task Two_replicas_proposing_the_same_change_advance_the_generation_once()
    {
        var coordinator = Coordinator();
        await coordinator.TrySeedAsync(SchedulerRunMode.Direct, "worker-1", default);

        var races = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(index =>
                coordinator.TryProposeAsync(
                    SchedulerRunMode.Queue, expectedGeneration: 1, $"worker-{index}", default)));

        // One generation, not eight. Every generation costs the whole fleet a drain, and eight of
        // them for one decision would be seven handovers nobody asked for.
        races.Count(proposed => proposed).Should().Be(1);
        (await coordinator.ReadFleetAsync(Generous, default)).Record!.Generation.Should().Be(2);
    }

    [Fact]
    public async Task A_proposal_against_a_generation_that_has_moved_is_refused()
    {
        var coordinator = Coordinator();
        await coordinator.TrySeedAsync(SchedulerRunMode.Direct, "worker-1", default);
        await coordinator.TryProposeAsync(SchedulerRunMode.Queue, 1, "worker-1", default);

        // A replica acting on a record it read before somebody else's change. Overwriting here would
        // discard a decision it never saw.
        var stale = await coordinator.TryProposeAsync(SchedulerRunMode.Direct, 1, "worker-2", default);

        stale.Should().BeFalse();

        var record = (await coordinator.ReadFleetAsync(Generous, default)).Record!;
        record.DesiredMode.Should().Be(SchedulerRunMode.Queue);
        record.Generation.Should().Be(2);
    }

    [Fact]
    public async Task Proposing_the_mode_already_in_force_costs_the_fleet_nothing()
    {
        var coordinator = Coordinator();
        await coordinator.TrySeedAsync(SchedulerRunMode.Queue, "worker-1", default);

        var proposed = await coordinator.TryProposeAsync(SchedulerRunMode.Queue, 1, "worker-1", default);

        // Otherwise every replica whose configuration matches would propose a generation on every
        // pass, and the fleet would drain every few seconds forever.
        proposed.Should().BeFalse();
        (await coordinator.ReadFleetAsync(Generous, default)).Record!.Generation.Should().Be(1);
    }

    [Fact]
    public async Task A_replica_with_a_wrong_clock_is_still_seen_as_alive()
    {
        // The reason heartbeats are stamped with $currentDate. This replica believes it is 2020; if
        // its own clock reached the document, the rest of the fleet would read it as long expired and
        // move to a new mode while it was still working.
        var coordinator = Coordinator(new ControlledTimeProvider(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);

        var view = await coordinator.ReadFleetAsync(TimeSpan.FromMinutes(1), default);

        view.LiveReplicas.Should().ContainSingle().Which.WorkerName.Should().Be("worker-1");
    }

    [Fact]
    public async Task A_replica_that_has_stopped_reporting_drops_out_of_the_fleet()
    {
        var coordinator = Coordinator();

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);
        await coordinator.ReportAsync(
            "worker-2", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);

        // Backdated in the database rather than waited out, because the window a real deployment uses
        // is fifteen minutes and the arithmetic is the same either way.
        await Replicas().UpdateOneAsync(
            replica => replica.WorkerName == "worker-2",
            Builders<SubscriptionSchedulerReplica>.Update.Set(
                replica => replica.HeartbeatAtUtc, DateTime.UtcNow.AddMinutes(-10)));

        var view = await coordinator.ReadFleetAsync(TimeSpan.FromMinutes(1), default);

        view.LiveReplicas.Should().ContainSingle().Which.WorkerName.Should().Be("worker-1");

        // And it is no longer somebody the rest of the fleet waits for — which is only safe because
        // a replica stops itself a margin inside this same window.
        view.MayActivate(generation: 2, exceptWorkerName: "worker-1").Should().BeTrue();
    }

    [Fact]
    public async Task A_replica_that_is_still_reporting_holds_a_mode_change_up()
    {
        var coordinator = Coordinator();

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 2,
            SchedulerReplicaState.Drained, default);
        await coordinator.ReportAsync(
            "worker-2", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);

        var view = await coordinator.ReadFleetAsync(Generous, default);

        view.MayActivate(generation: 2, exceptWorkerName: "worker-1").Should().BeFalse();
        view.Blockers(generation: 2, exceptWorkerName: "worker-1").Should().Equal(["worker-2"]);
    }

    [Fact]
    public async Task A_replica_reporting_again_updates_its_state_rather_than_joining_twice()
    {
        var coordinator = Coordinator();

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);

        var first = (await coordinator.ReadFleetAsync(Generous, default)).LiveReplicas.Single();

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Queue, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Draining, default);

        var view = await coordinator.ReadFleetAsync(Generous, default);
        var replica = view.LiveReplicas.Should().ContainSingle().Subject;

        replica.State.Should().Be(SchedulerReplicaState.Draining);
        replica.ConfiguredMode.Should().Be(SchedulerRunMode.Queue);
        // Kept from the first report, so how long a pod has been up is answerable from the roster.
        replica.StartedAtUtc.Should().Be(first.StartedAtUtc);
    }

    [Fact]
    public async Task A_replica_that_stops_politely_leaves_the_roster_immediately()
    {
        var coordinator = Coordinator();

        await coordinator.ReportAsync(
            "worker-1", SchedulerRunMode.Direct, SchedulerRunMode.Direct, 1,
            SchedulerReplicaState.Running, default);
        await coordinator.RemoveAsync("worker-1", default);

        // A planned restart otherwise costs the fleet a full expiry window before it can move.
        (await coordinator.ReadFleetAsync(Generous, default)).LiveReplicas.Should().BeEmpty();
    }

    [Fact]
    public async Task Index_creation_is_safe_to_repeat()
    {
        var coordinator = Coordinator();

        await coordinator.EnsureIndexesAsync(default);
        await coordinator.EnsureIndexesAsync(default);

        var index = (await Replicas().Indexes.List().ToListAsync())
            .Single(candidate =>
                candidate["name"].AsString ==
                SubscriptionSchedulerCoordinationIndexNames.ReplicaHeartbeat);

        // Both jobs in one index, which is not a tidiness point: asking for two on the same key is
        // rejected outright, and the second call has to be a no-op rather than an error worker
        // startup logs and moves past.
        index.Contains("expireAfterSeconds").Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_fleet_reads_as_empty_rather_than_failing()
    {
        // What every worker sees on the first pass in a fresh environment, and the branch that
        // decides to seed. A throw here would be an outage on startup rather than a first pass.
        var view = await Coordinator().ReadFleetAsync(Generous, default);

        view.Record.Should().BeNull();
        view.LiveReplicas.Should().BeEmpty();
    }

    private IMongoCollection<SubscriptionSchedulerModeRecord> Modes() =>
        _fixture.Collection<SubscriptionSchedulerModeRecord>("SubscriptionSchedulerMode");

    private IMongoCollection<SubscriptionSchedulerReplica> Replicas() =>
        _fixture.Collection<SubscriptionSchedulerReplica>("SubscriptionSchedulerReplicas");

    private SubscriptionSchedulerCoordinator Coordinator(TimeProvider? time = null)
    {
        var secret = new Mock<IBlocksSecret>();
        secret.SetupGet(value => value.DatabaseConnectionString)
            .Returns(MongoIntegrationFixture.ConnectionString);
        secret.SetupGet(value => value.RootDatabaseName).Returns(_fixture.DatabaseName);

        return new SubscriptionSchedulerCoordinator(
            _fixture.DbContextProvider, secret.Object, time ?? TimeProvider.System);
    }
}
