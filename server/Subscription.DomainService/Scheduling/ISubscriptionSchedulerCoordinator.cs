namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The fleet's shared record of which mode background work runs in, in the root database.
/// </summary>
/// <remarks>
/// Storage and nothing else. What to do with what it returns is
/// <see cref="SubscriptionSchedulerFleetSynchronizer"/>'s decision, so the protocol can be tested
/// without a database and the database can be tested without the protocol.
/// </remarks>
public interface ISubscriptionSchedulerCoordinator
{
    Task EnsureIndexesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the mode record and every replica still considered alive, in that order.
    /// </summary>
    /// <param name="replicaExpiry">
    /// How long a replica may go without a heartbeat before the fleet stops waiting for it.
    /// Evaluated against the <em>database's</em> clock, so a replica whose own clock is wrong cannot
    /// appear alive when it is gone, or gone while it is still working.
    /// </param>
    Task<SchedulerFleetView> ReadFleetAsync(
        TimeSpan replicaExpiry,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the first record, if there is none. False when another replica got there first.
    /// </summary>
    Task<bool> TrySeedAsync(
        SchedulerRunMode mode,
        string workerName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the fleet to a new mode, one generation at a time.
    /// </summary>
    /// <remarks>
    /// Conditional on the generation the caller read, so two replicas proposing the same change at
    /// the same instant produce one generation rather than two — and a proposal written against a
    /// record that has since moved fails instead of overwriting a decision it never saw.
    /// </remarks>
    Task<bool> TryProposeAsync(
        SchedulerRunMode mode,
        long expectedGeneration,
        string workerName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publishes this replica's own state, stamped with the database's clock.
    /// </summary>
    Task ReportAsync(
        string workerName,
        SchedulerRunMode configuredMode,
        SchedulerRunMode activeMode,
        long generation,
        SchedulerReplicaState state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes this replica's row on a clean shutdown, so it stops holding a mode change up.
    /// </summary>
    /// <remarks>
    /// Best effort by nature — a pod that is killed cannot do this, which is what the expiry window
    /// is for. Doing it when we can turns the common case of a planned restart from a wait into an
    /// immediate handover.
    /// </remarks>
    Task RemoveAsync(string workerName, CancellationToken cancellationToken);
}
