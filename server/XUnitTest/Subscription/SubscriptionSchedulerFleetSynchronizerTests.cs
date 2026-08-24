using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The fleet handshake: when a replica may work, and when it has to wait for the others.
/// </summary>
/// <remarks>
/// The property every test here defends is one sentence long — <b>no two replicas execute the same
/// background work in different modes</b> — and it is the reason changing the mode used to require
/// stopping every worker. What makes it defensible without a database is that the protocol reads a
/// view and writes a report, so the interesting states can simply be handed to it.
/// </remarks>
public sealed class SubscriptionSchedulerFleetSynchronizerTests
{
    private const string Me = "worker-1";
    private const string Other = "worker-2";

    private readonly Mock<ISubscriptionSchedulerCoordinator> _coordinator = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

    private SchedulerFleetView _view = new(null, []);

    public SubscriptionSchedulerFleetSynchronizerTests() =>
        _coordinator
            .Setup(coordinator => coordinator.ReadFleetAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _view);

    [Fact]
    public async Task A_fleet_with_no_record_gets_one_from_the_first_replica_to_look()
    {
        _coordinator
            .Setup(coordinator => coordinator.TrySeedAsync(
                It.IsAny<SchedulerRunMode>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var gate = Gate(queueDriven: true);

        await Synchronizer(gate).SyncAsync(Me, default);

        _coordinator.Verify(
            coordinator => coordinator.TrySeedAsync(
                SchedulerRunMode.Queue, Me, It.IsAny<CancellationToken>()),
            Times.Once);

        // Not yet working: the record is written but unread, and acting on what we believe we just
        // wrote is how a replica that lost the race runs the wrong mode for a pass.
        gate.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_replica_runs_the_mode_the_fleet_agreed_rather_than_its_own_configuration()
    {
        // The whole point. This replica has been rolled out with the queue enabled while the fleet is
        // still executing directly; running its own configuration now is the double execution that
        // used to make a mode change need a full stop.
        _view = View(SchedulerRunMode.Direct, generation: 4);

        var gate = Gate(queueDriven: true);

        await Synchronizer(gate).SyncAsync(Me, default);

        gate.IsOpen.Should().BeTrue();
        gate.ActiveMode.Should().Be(SchedulerRunMode.Direct);
        gate.ActiveGeneration.Should().Be(4);
    }

    [Fact]
    public async Task A_replica_will_not_start_while_another_is_still_behind_the_generation()
    {
        // worker-2 has not caught up, so it may still be executing the previous mode. Starting here
        // would put two modes on the same work at the same instant.
        _view = View(
            SchedulerRunMode.Queue,
            generation: 5,
            Replica(Other, generation: 4, SchedulerReplicaState.Running));

        var gate = Gate(queueDriven: true);

        await Synchronizer(gate).SyncAsync(Me, default);

        gate.IsOpen.Should().BeFalse();

        // And it says so, at the new generation, so it stops being the one everybody waits for.
        _coordinator.Verify(
            coordinator => coordinator.ReportAsync(
                Me, It.IsAny<SchedulerRunMode>(), It.IsAny<SchedulerRunMode>(), 5,
                SchedulerReplicaState.Drained, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_draining_replica_is_still_waited_for_even_though_it_takes_no_new_work()
    {
        // Not Running, and not finished either. Read as "gone" this is the gap the whole barrier
        // would leak through: the last renewal of the old mode overlapping the first of the new.
        _view = View(
            SchedulerRunMode.Queue,
            generation: 5,
            Replica(Other, generation: 4, SchedulerReplicaState.Draining));

        var gate = Gate(queueDriven: true);

        await Synchronizer(gate).SyncAsync(Me, default);

        gate.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Once_nobody_is_behind_the_new_generation_the_replica_starts_in_the_new_mode()
    {
        _view = View(
            SchedulerRunMode.Queue,
            generation: 5,
            Replica(Other, generation: 5, SchedulerReplicaState.Drained));

        var gate = Gate(queueDriven: true);

        await Synchronizer(gate).SyncAsync(Me, default);

        gate.IsOpen.Should().BeTrue();
        gate.ActiveMode.Should().Be(SchedulerRunMode.Queue);
        gate.ActiveGeneration.Should().Be(5);

        _coordinator.Verify(
            coordinator => coordinator.ReportAsync(
                Me, It.IsAny<SchedulerRunMode>(), SchedulerRunMode.Queue, 5,
                SchedulerReplicaState.Running, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_replica_holding_work_reports_the_generation_it_is_still_in()
    {
        // The handover half of the barrier. This replica has a renewal in flight in the old mode, so
        // it must keep the fleet waiting — reporting the new generation here would tell everybody
        // else it had finished while a provider call was still open.
        _view = View(SchedulerRunMode.Direct, generation: 1);

        var gate = Gate(queueDriven: false);
        var synchronizer = Synchronizer(gate);

        await synchronizer.SyncAsync(Me, default);

        using var ticket = gate.TryBegin(SchedulerRunMode.Direct);
        ticket.Should().NotBeNull();

        _view = View(SchedulerRunMode.Queue, generation: 2);
        await synchronizer.SyncAsync(Me, default);

        gate.IsOpen.Should().BeFalse("no new work while a mode change is pending");
        gate.ActiveGeneration.Should().Be(1, "the generation it is still actually in");

        _coordinator.Verify(
            coordinator => coordinator.ReportAsync(
                Me, It.IsAny<SchedulerRunMode>(), It.IsAny<SchedulerRunMode>(), 1,
                SchedulerReplicaState.Draining, It.IsAny<CancellationToken>()),
            Times.Once);

        // And once the work finishes, the next pass hands over.
        ticket!.Dispose();
        await synchronizer.SyncAsync(Me, default);

        gate.IsOpen.Should().BeTrue();
        gate.ActiveMode.Should().Be(SchedulerRunMode.Queue);
        gate.ActiveGeneration.Should().Be(2);
    }

    [Fact]
    public async Task A_change_is_proposed_only_when_every_live_replica_is_configured_for_it()
    {
        // The anti-flap rule. Mid-roll, half the fleet still holds the old configuration, and a
        // proposal now would move the fleet on the strength of one pod — then back again when a pod
        // on the old configuration restarts.
        var gate = Gate(queueDriven: true);
        var synchronizer = Synchronizer(gate);

        _view = View(
            SchedulerRunMode.Direct,
            generation: 3,
            Replica(Me, generation: 3, SchedulerReplicaState.Running, SchedulerRunMode.Queue),
            Replica(Other, generation: 3, SchedulerReplicaState.Running, SchedulerRunMode.Direct));

        await Settle(synchronizer, gate, SchedulerRunMode.Direct, generation: 3);
        await synchronizer.SyncAsync(Me, default);

        _coordinator.Verify(
            coordinator => coordinator.TryProposeAsync(
                It.IsAny<SchedulerRunMode>(), It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_change_is_proposed_once_the_whole_fleet_holds_the_new_configuration()
    {
        var gate = Gate(queueDriven: true);
        var synchronizer = Synchronizer(gate);

        _view = View(
            SchedulerRunMode.Direct,
            generation: 3,
            Replica(Me, generation: 3, SchedulerReplicaState.Running, SchedulerRunMode.Queue),
            Replica(Other, generation: 3, SchedulerReplicaState.Running, SchedulerRunMode.Queue));

        await Settle(synchronizer, gate, SchedulerRunMode.Direct, generation: 3);
        await synchronizer.SyncAsync(Me, default);

        // Against the generation it read, so two replicas proposing the same change at the same
        // instant produce one generation to drain for rather than two.
        _coordinator.Verify(
            coordinator => coordinator.TryProposeAsync(
                SchedulerRunMode.Queue, 3, Me, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Nothing_is_proposed_while_the_fleet_is_still_taking_up_the_last_change()
    {
        // Everybody is configured for Queue and the record already says Queue is coming, but one
        // replica has not reached it. Another proposal here would restart the drain for a generation
        // nobody finished reaching.
        var gate = Gate(queueDriven: true);
        var synchronizer = Synchronizer(gate);

        await Settle(synchronizer, gate, SchedulerRunMode.Direct, generation: 3);

        // Reachable exactly this way: this replica is already running at 3 when a replica still
        // working its way up from 2 appears — a pod that has just restarted, or one draining slowly.
        _view = View(
            SchedulerRunMode.Direct,
            generation: 3,
            Replica(Me, generation: 3, SchedulerReplicaState.Running, SchedulerRunMode.Queue),
            Replica(Other, generation: 2, SchedulerReplicaState.Draining, SchedulerRunMode.Queue));

        await synchronizer.SyncAsync(Me, default);

        _coordinator.Verify(
            coordinator => coordinator.TryProposeAsync(
                It.IsAny<SchedulerRunMode>(), It.IsAny<long>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Coordination_going_down_does_not_stop_work_that_is_already_running()
    {
        // A root database blip must not stop billing. Nothing can be flipping that this replica does
        // not know about, because a flip cannot complete without its own acknowledgement.
        _view = View(SchedulerRunMode.Direct, generation: 1);

        var gate = Gate(queueDriven: false);
        var synchronizer = Synchronizer(gate);

        await synchronizer.SyncAsync(Me, default);
        gate.IsOpen.Should().BeTrue();

        _coordinator
            .Setup(coordinator => coordinator.ReadFleetAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        _time.Advance(TimeSpan.FromMinutes(5));
        await synchronizer.SyncAsync(Me, default);

        gate.IsOpen.Should().BeTrue("five minutes is inside the window the fleet still waits out");
    }

    [Fact]
    public async Task A_replica_stops_itself_before_the_fleet_stops_waiting_for_it()
    {
        // The ordering the expiry rests on. If a replica could still be working when the others
        // decide it is gone, the barrier would be a formality — so it gives up first.
        _view = View(SchedulerRunMode.Direct, generation: 1);

        var gate = Gate(queueDriven: false);
        var synchronizer = Synchronizer(gate);

        await synchronizer.SyncAsync(Me, default);

        _coordinator
            .Setup(coordinator => coordinator.ReadFleetAsync(
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        _time.Advance(synchronizer.SilenceDeadline + TimeSpan.FromSeconds(1));
        await synchronizer.SyncAsync(Me, default);

        gate.IsOpen.Should().BeFalse();

        // And strictly before the fleet's own window, or the two could overlap.
        synchronizer.SilenceDeadline.Should().BeLessThan(synchronizer.ReplicaExpiry);
    }

    [Fact]
    public async Task A_failed_report_counts_as_silence_even_when_the_record_can_still_be_read()
    {
        // Reading the roster is not being in it. A replica whose own row has stopped being written is
        // one the others will stop waiting for, however well it can see them.
        _view = View(SchedulerRunMode.Direct, generation: 1);

        var gate = Gate(queueDriven: false);
        var synchronizer = Synchronizer(gate);

        await synchronizer.SyncAsync(Me, default);

        _coordinator
            .Setup(coordinator => coordinator.ReportAsync(
                It.IsAny<string>(), It.IsAny<SchedulerRunMode>(), It.IsAny<SchedulerRunMode>(),
                It.IsAny<long>(), It.IsAny<SchedulerReplicaState>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        _time.Advance(synchronizer.SilenceDeadline + TimeSpan.FromSeconds(1));
        await synchronizer.SyncAsync(Me, default);

        gate.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_replica_that_stops_politely_stops_holding_the_fleet_up()
    {
        await Synchronizer(Gate(queueDriven: false)).WithdrawAsync(Me, default);

        // Otherwise a planned restart costs the fleet a full expiry window before it can move.
        _coordinator.Verify(
            coordinator => coordinator.RemoveAsync(Me, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_shutdown_that_cannot_reach_the_database_still_shuts_down()
    {
        _coordinator
            .Setup(coordinator => coordinator.RemoveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        // The expiry window is the fallback for a pod that could not say goodbye, so failing here
        // would be swapping a delayed handover for a failed shutdown.
        await Synchronizer(Gate(queueDriven: false))
            .Invoking(synchronizer => synchronizer.WithdrawAsync(Me, default))
            .Should().NotThrowAsync();
    }

    /// <summary>
    /// Brings a fresh replica up to the fleet's generation, which takes one pass.
    /// </summary>
    /// <remarks>
    /// Deliberately not shortcut: a process starts believing nothing, and the first pass is the one
    /// where it drains, reports and joins. Tests about what a <em>running</em> replica does have to
    /// go through it, or they are testing the joining path by accident.
    /// </remarks>
    private async Task Settle(
        SubscriptionSchedulerFleetSynchronizer synchronizer,
        SubscriptionSchedulerModeGate gate,
        SchedulerRunMode mode,
        long generation)
    {
        var pending = _view;

        _view = View(mode, generation);
        await synchronizer.SyncAsync(Me, default);

        gate.ActiveGeneration.Should().Be(generation, "the replica has to be running to propose");

        _view = pending;
    }

    private SubscriptionSchedulerModeGate Gate(bool queueDriven)
    {
        var options = Options.Create(new SubscriptionOptions
        {
            SchedulerEnabled = queueDriven,
            SchedulerCoordinationEnabled = true
        });

        return new SubscriptionSchedulerModeGate(
            new SubscriptionSchedulerMode(
                options, NullLogger<SubscriptionSchedulerMode>.Instance),
            NullLogger<SubscriptionSchedulerModeGate>.Instance);
    }

    private SubscriptionSchedulerFleetSynchronizer Synchronizer(
        SubscriptionSchedulerModeGate gate) => new(
        _coordinator.Object,
        gate,
        Options.Create(new SubscriptionOptions
        {
            SchedulerEnabled = gate.ConfiguredMode == SchedulerRunMode.Queue,
            SchedulerCoordinationEnabled = true
        }),
        NullLogger<SubscriptionSchedulerFleetSynchronizer>.Instance,
        _time);

    private static SchedulerFleetView View(
        SchedulerRunMode desired,
        long generation,
        params SubscriptionSchedulerReplica[] replicas) => new(
        new SubscriptionSchedulerModeRecord
        {
            DesiredMode = desired,
            Generation = generation,
            ProposedBy = Other,
            ProposedAtUtc = new DateTime(2026, 8, 24, 11, 0, 0, DateTimeKind.Utc)
        },
        replicas);

    private static SubscriptionSchedulerReplica Replica(
        string workerName,
        long generation,
        SchedulerReplicaState state,
        SchedulerRunMode configured = SchedulerRunMode.Direct) => new()
    {
        WorkerName = workerName,
        ConfiguredMode = configured,
        ActiveMode = SchedulerRunMode.Direct,
        Generation = generation,
        State = state,
        HeartbeatAtUtc = new DateTime(2026, 8, 24, 11, 59, 55, DateTimeKind.Utc)
    };
}
