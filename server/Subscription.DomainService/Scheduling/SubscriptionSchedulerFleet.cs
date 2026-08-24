using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Scheduling;

/// <summary>Which of the two ways of doing background work a process is running.</summary>
public enum SchedulerRunMode
{
    /// <summary>The reconciliation sweep executes work itself, as it did before the queue existed.</summary>
    Direct = 0,

    /// <summary>The sweep only schedules, and the durable queue is drained by the dispatcher.</summary>
    Queue = 1
}

/// <summary>Where a replica is in a mode change.</summary>
public enum SchedulerReplicaState
{
    /// <summary>Doing work in <see cref="SubscriptionSchedulerReplica.ActiveMode"/>.</summary>
    Running = 0,

    /// <summary>Taking no new work, but something started earlier is still finishing.</summary>
    Draining = 1,

    /// <summary>Doing nothing, and holding nothing. Safe for the fleet to move past.</summary>
    Drained = 2
}

/// <summary>
/// The fleet's one answer to "which mode is subscription background work running in".
/// </summary>
/// <remarks>
/// A single document, so there is nothing to reconcile between copies. <see cref="Generation"/> is
/// what replicas actually coordinate on: the mode alone cannot distinguish "we have always been in
/// Direct" from "we were moved to Queue and back", and a replica that missed the round trip would
/// otherwise believe it was already in step.
/// </remarks>
public sealed class SubscriptionSchedulerModeRecord
{
    /// <summary>Fixed, so the collection can only ever hold one of these.</summary>
    public const string SingletonId = "subscription";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    public SchedulerRunMode DesiredMode { get; set; }

    /// <summary>Incremented once per mode change, and never reused.</summary>
    public long Generation { get; set; }

    /// <summary>The worker that proposed this generation, for the log trail.</summary>
    public string ProposedBy { get; set; } = string.Empty;

    public DateTime ProposedAtUtc { get; set; }
}

/// <summary>
/// One worker process, as the rest of the fleet sees it.
/// </summary>
/// <remarks>
/// Keyed by worker name, so a pod that restarts reuses its own row rather than accumulating one per
/// life. Every field here exists to answer a question another replica has to ask before it changes
/// mode: what are you configured for, what are you actually running, and are you still there.
/// </remarks>
public sealed class SubscriptionSchedulerReplica
{
    [BsonId]
    public string WorkerName { get; set; } = string.Empty;

    /// <summary>What this replica's own configuration says it should be running.</summary>
    public SchedulerRunMode ConfiguredMode { get; set; }

    /// <summary>What it is running now, which is only the same once the fleet has moved.</summary>
    public SchedulerRunMode ActiveMode { get; set; }

    /// <summary>The generation this replica has reached. Never higher than the record's.</summary>
    public long Generation { get; set; }

    public SchedulerReplicaState State { get; set; }

    /// <summary>Written with the database's clock, so replica clock skew cannot fake liveness.</summary>
    public DateTime HeartbeatAtUtc { get; set; }

    public DateTime StartedAtUtc { get; set; }
}

/// <summary>The record and every replica still considered alive, read together.</summary>
/// <remarks>
/// Together, deliberately: deciding whether to move on a record read at one instant and a roster
/// read at another is deciding on a fleet that never existed.
/// </remarks>
public sealed record SchedulerFleetView(
    SubscriptionSchedulerModeRecord? Record,
    IReadOnlyList<SubscriptionSchedulerReplica> LiveReplicas)
{
    /// <summary>
    /// Whether this process may run at <paramref name="generation"/> yet.
    /// </summary>
    /// <remarks>
    /// The whole barrier, in one condition: no other live replica may still be behind this
    /// generation.
    /// <para>
    /// State is deliberately not part of the test. A replica reports the new generation only once it
    /// holds nothing, so <em>behind</em> covers both a replica still running the old mode and one
    /// still finishing a unit of work it started there — and reading the state instead would let a
    /// draining replica's last renewal overlap the new mode's first.
    /// </para>
    /// <para>
    /// It covers both cases that matter without telling them apart. Joining a fleet already settled
    /// here is allowed, because everyone else reports this generation too. Completing a mode change
    /// is allowed only once the last replica has left the previous one — which is what stops a rolled
    /// pod from draining the queue while a pod nobody has restarted yet is still executing the same
    /// work directly.
    /// </para>
    /// </remarks>
    public bool MayActivate(long generation, string exceptWorkerName) =>
        Blockers(generation, exceptWorkerName).Count == 0;

    /// <summary>Replicas still behind this generation, for the log line that says why.</summary>
    public IReadOnlyList<string> Blockers(long generation, string exceptWorkerName) =>
        [.. LiveReplicas
            .Where(replica =>
                // Excluded, because a replica cannot be its own reason to wait: its row may still
                // hold whatever it reported before it drained.
                !string.Equals(replica.WorkerName, exceptWorkerName, StringComparison.Ordinal) &&
                replica.Generation < generation)
            .Select(replica => replica.WorkerName)];

    /// <summary>
    /// Whether the fleet has finished moving, so a new proposal would not interrupt one.
    /// </summary>
    public bool Settled(long generation) =>
        LiveReplicas.All(replica => replica.Generation == generation);

    /// <summary>
    /// Whether every live replica is configured for <paramref name="mode"/>.
    /// </summary>
    /// <remarks>
    /// The anti-flap rule. A mode change is proposed only when the whole fleet's configuration
    /// already agrees, so a change takes effect once its deployment has finished rolling rather than
    /// the moment the first new pod reports for duty — and a single pod left on stale configuration
    /// can never drag the fleet back.
    /// </remarks>
    public bool UnanimouslyConfiguredFor(SchedulerRunMode mode) =>
        LiveReplicas.Count > 0 && LiveReplicas.All(replica => replica.ConfiguredMode == mode);
}

/// <summary>Named so a migration can find them and an operator can recognize them.</summary>
public static class SubscriptionSchedulerCoordinationIndexNames
{
    /// <summary>Serves the liveness query and expires the row. One index, both jobs.</summary>
    public const string ReplicaHeartbeat = "ix_subsched_replica_heartbeat";
}
