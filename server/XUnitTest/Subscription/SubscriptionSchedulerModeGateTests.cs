using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The one thing both hosted services ask before doing anything.
/// </summary>
/// <remarks>
/// Small enough to read in a minute, and worth its own tests because two failures here are silent:
/// a gate that starts open lets a freshly rolled replica work before the fleet has agreed anything,
/// and a ticket that is not counted lets a handover complete while a provider call is still open.
/// </remarks>
public sealed class SubscriptionSchedulerModeGateTests
{
    [Fact]
    public void With_coordination_off_the_gate_is_simply_open_at_the_configured_mode()
    {
        // The behaviour that existed before any of this: configuration is the decision, and nothing
        // can pause the process. Coordination has to be adoptable without changing today's fleet.
        var gate = Gate(queueDriven: true, coordinationEnabled: false);

        gate.IsOpen.Should().BeTrue();
        gate.ActiveMode.Should().Be(SchedulerRunMode.Queue);
        gate.TryBegin(SchedulerRunMode.Queue).Should().NotBeNull();
    }

    [Fact]
    public void With_coordination_on_the_gate_starts_shut()
    {
        // A replica that worked before it knew the fleet's generation would be the exact failure the
        // fleet record exists to prevent, so doing nothing is the safe starting state.
        var gate = Gate(queueDriven: true, coordinationEnabled: true);

        gate.IsOpen.Should().BeFalse();
        gate.ActiveGeneration.Should().Be(-1, "no generation has been agreed with anybody yet");
        gate.TryBegin(SchedulerRunMode.Queue).Should().BeNull();
    }

    [Fact]
    public void Work_is_refused_for_a_mode_the_process_is_not_in()
    {
        var gate = Gate(queueDriven: false, coordinationEnabled: true);
        gate.Activate(SchedulerRunMode.Direct, 7);

        // The caller reads the mode and then asks for it, so this is the window where the two
        // disagree. Refusing is what makes that window harmless.
        gate.TryBegin(SchedulerRunMode.Queue).Should().BeNull();
        gate.TryBegin(SchedulerRunMode.Direct).Should().NotBeNull();
    }

    [Fact]
    public void Closing_the_gate_stops_new_work_and_leaves_work_in_flight_alone()
    {
        var gate = Gate(queueDriven: false, coordinationEnabled: true);
        gate.Activate(SchedulerRunMode.Direct, 1);

        var ticket = gate.TryBegin(SchedulerRunMode.Direct);
        gate.InFlight.Should().Be(1);

        gate.Close("mode change");

        gate.IsOpen.Should().BeFalse();
        gate.TryBegin(SchedulerRunMode.Direct).Should().BeNull();

        // Still counted. A handover that ignored this would be an interruption: the renewal in this
        // ticket is mid-provider-call, and the fleet is about to be told nobody is holding anything.
        gate.InFlight.Should().Be(1);

        ticket!.Dispose();
        gate.InFlight.Should().Be(0);
    }

    [Fact]
    public void A_ticket_disposed_twice_only_counts_once()
    {
        // `using` plus an explicit Dispose in a retry path is easy to write by accident, and an
        // in-flight count that drifts below zero would let a handover complete during live work.
        var gate = Gate(queueDriven: false, coordinationEnabled: true);
        gate.Activate(SchedulerRunMode.Direct, 1);

        var first = gate.TryBegin(SchedulerRunMode.Direct);
        var second = gate.TryBegin(SchedulerRunMode.Direct);

        first!.Dispose();
        first.Dispose();

        gate.InFlight.Should().Be(1, "the second ticket is still outstanding");

        second!.Dispose();
        gate.InFlight.Should().Be(0);
    }

    [Fact]
    public void Reactivating_at_the_same_generation_changes_nothing()
    {
        // Every pass calls this while the fleet is settled, so it has to be idempotent — and the
        // generation is part of the identity, or a flip away and back would look like no change.
        var gate = Gate(queueDriven: true, coordinationEnabled: true);

        gate.Activate(SchedulerRunMode.Queue, 4);
        gate.Activate(SchedulerRunMode.Queue, 4);

        gate.IsOpen.Should().BeTrue();
        gate.ActiveGeneration.Should().Be(4);

        gate.Activate(SchedulerRunMode.Direct, 5);

        gate.ActiveMode.Should().Be(SchedulerRunMode.Direct);
        gate.ActiveGeneration.Should().Be(5);
    }

    [Fact]
    public void Activating_reopens_a_gate_that_was_closed()
    {
        // The recovery path after a database blip fenced this replica: coordination comes back, the
        // fleet's generation is confirmed, and work resumes without a restart.
        var gate = Gate(queueDriven: false, coordinationEnabled: true);
        gate.Activate(SchedulerRunMode.Direct, 1);
        gate.Close("coordination unreachable");

        gate.Activate(SchedulerRunMode.Direct, 1);

        gate.IsOpen.Should().BeTrue();
        gate.ClosedReason.Should().BeEmpty();
    }

    private static SubscriptionSchedulerModeGate Gate(bool queueDriven, bool coordinationEnabled)
    {
        var options = Options.Create(new SubscriptionOptions
        {
            SchedulerEnabled = queueDriven,
            SchedulerCoordinationEnabled = coordinationEnabled
        });

        return new SubscriptionSchedulerModeGate(
            new SubscriptionSchedulerMode(options, NullLogger<SubscriptionSchedulerMode>.Instance),
            NullLogger<SubscriptionSchedulerModeGate>.Instance);
    }
}
