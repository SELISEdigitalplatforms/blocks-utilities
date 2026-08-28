using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// One drainer's own account of itself, in the root database so another process can read it.
/// </summary>
/// <remarks>
/// This exists because readiness was being answered in the wrong process. The health check lives in
/// the Api and the drainer lives in the Worker, and they share no memory &#8212; so a per-process
/// readiness object read from the Api was always in its pristine starting state, and the endpoint
/// reported that work was draining on the strength of the Api's own ability to reach MongoDB. Every
/// Worker replica could be dead and it would still have said so.
/// <para>
/// Written to the same root database the queue is in, on purpose. Anything that can claim work can
/// write one of these, so a heartbeat that stops arriving means either the replica is gone or it has
/// lost the database &#8212; and in both cases nothing is draining, which is what the reader needs to
/// know.
/// </para>
/// <para>
/// Carries no tenant data and no secrets. It says which process, since when, and whether its claims
/// are getting through.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionQueueWorker
{
    /// <summary>
    /// Stable for the life of the process: machine, process id, and a per-start suffix.
    /// </summary>
    /// <remarks>
    /// The suffix matters. Process ids are reused, and a pod restarting into the same id would
    /// otherwise inherit the dead process's record and its failure history.
    /// </remarks>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string WorkerId { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Last time this replica said anything at all, whether or not it was working.</summary>
    /// <remarks>
    /// Separate from the claim stamp below, because they answer different questions. A heartbeat
    /// without a recent claim is a replica that is alive and failing; no heartbeat at all is a
    /// replica that is gone. Reporting those the same way would send somebody to the wrong place.
    /// </remarks>
    public DateTime HeartbeatAtUtc { get; set; }

    /// <summary>Last time a claim reached the queue, including one that found nothing.</summary>
    /// <remarks>
    /// An empty claim counts. The question is whether the queue is being drained, and a reachable
    /// queue with nothing in it is the healthiest state there is.
    /// </remarks>
    public DateTime? LastClaimSucceededAtUtc { get; set; }

    /// <summary>Last time this replica actually ran work, as opposed to finding none.</summary>
    public DateTime? LastBatchProcessedAtUtc { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTime? LastFailureAtUtc { get; set; }

    /// <summary>
    /// A classification, never a provider or driver message.
    /// </summary>
    /// <remarks>
    /// This record is readable across every tenant, so an unbounded string copied from an exception
    /// is a place for connection strings and host names to end up.
    /// </remarks>
    public string? LastFailureClassification { get; set; }

    /// <summary>When this record may be purged after the replica stops reporting.</summary>
    /// <remarks>
    /// A TTL rather than a delete on shutdown: a replica that is killed never gets to tidy up, and a
    /// registry that only removed records politely would fill with the ones that crashed. Set on
    /// every heartbeat, so the record outlives the process by the retention and no longer.
    /// </remarks>
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// What the fleet looks like right now, for a readiness check in another process.
/// </summary>
/// <param name="LiveWorkers">
/// Replicas whose heartbeat is inside the liveness window. Zero is the state that used to report
/// healthy.
/// </param>
/// <param name="DrainingWorkers">
/// Live replicas whose claims are also getting through. A live replica that cannot claim is alive
/// and useless, and the difference between these two numbers is the whole point of the record.
/// </param>
/// <param name="NewestClaimAtUtc">
/// The most recent successful claim anywhere in the fleet, or null if nobody has managed one.
/// </param>
public sealed record SubscriptionQueueFleetHealth(
    int LiveWorkers,
    int DrainingWorkers,
    DateTime? NewestHeartbeatAtUtc,
    DateTime? NewestClaimAtUtc,
    int WorstConsecutiveFailures,
    string? LastFailureClassification);
