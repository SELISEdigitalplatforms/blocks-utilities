using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// One pass of the fleet handshake: publish what this replica is doing, and move when the fleet has.
/// </summary>
/// <remarks>
/// The rules, in the order they are applied:
/// <list type="number">
/// <item>No record yet — write one from this replica's own configuration and stop for this pass.</item>
/// <item>
/// The record names a generation this replica has not reached — stop taking new work, wait for what
/// it already holds, then report the new generation. Once no other live replica is behind it, run in
/// the mode the record names. Not the mode this replica was configured for: the record is what the
/// fleet agreed, and a rolled pod running its own configuration early is the failure being prevented.
/// </item>
/// <item>
/// Settled, and every live replica's configuration disagrees with the record in the same direction —
/// propose the change. Unanimity is the anti-flap rule: a mode change takes effect when its
/// deployment has finished rolling, and one pod left on stale configuration cannot drag the fleet
/// back.
/// </item>
/// </list>
/// <para>
/// <b>Unreachable coordination does not stop work.</b> A replica that cannot read the record keeps
/// running in the mode it is already in — no transition can be in flight that it does not know
/// about, because a transition needs this replica's own acknowledgement to complete. What it must not
/// do is keep running past the point where the rest of the fleet stops waiting for it, so it stops
/// itself a safety margin before its own row expires. That ordering is the whole reason a
/// heartbeat expiry can be trusted: by the time others ignore a replica, it has already stopped.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerFleetSynchronizer
{
    private const int MinimumExpirySeconds = 60;

    private readonly ISubscriptionSchedulerCoordinator _coordinator;
    private readonly SubscriptionSchedulerModeGate _gate;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionSchedulerFleetSynchronizer> _logger;
    private readonly TimeProvider _time;

    /// <summary>When this replica last proved to the fleet that it is here.</summary>
    /// <remarks>
    /// A successful report, not a successful read: being able to see the roster is not the same as
    /// being in it, and it is being in it that stops the others moving on.
    /// </remarks>
    private DateTimeOffset _lastReported;

    private long _reportedGeneration = -1;
    private long _announcedProposalGeneration = -1;

    public SubscriptionSchedulerFleetSynchronizer(
        ISubscriptionSchedulerCoordinator coordinator,
        SubscriptionSchedulerModeGate gate,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionSchedulerFleetSynchronizer> logger,
        TimeProvider? time = null)
    {
        _coordinator = coordinator;
        _gate = gate;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _lastReported = _time.GetUtcNow();
    }

    /// <summary>How long a replica may be silent before the fleet stops waiting for it.</summary>
    public TimeSpan ReplicaExpiry => TimeSpan.FromSeconds(
        Math.Max(MinimumExpirySeconds, _options.Value.SchedulerReplicaExpirySeconds));

    /// <summary>
    /// The deadline this replica stops itself at, a margin before the fleet would stop waiting.
    /// </summary>
    /// <remarks>
    /// The margin covers the round trip and the clock the expiry is measured on, which is the
    /// database's rather than this process's. Shaped like the dispatcher's lease safety margin, and
    /// for the same reason: an owner has to give up slightly before the world assumes it has.
    /// </remarks>
    public TimeSpan SilenceDeadline
    {
        get
        {
            var expiry = ReplicaExpiry;
            var margin = TimeSpan.FromTicks(Math.Min(
                TimeSpan.FromSeconds(60).Ticks,
                expiry.Ticks / 4));

            return expiry - margin;
        }
    }

    public async Task SyncAsync(string workerName, CancellationToken cancellationToken)
    {
        SchedulerFleetView view;

        try
        {
            view = await _coordinator.ReadFleetAsync(ReplicaExpiry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FenceIfSilentTooLong(exception);

            return;
        }

        if (view.Record is null)
        {
            // Nothing to coordinate with yet. Seeded from this replica's configuration, and losing
            // the race to seed is not a failure — another replica's record says the same thing this
            // one would have, or names a mode this replica will drain into on the next pass.
            var seeded = await _coordinator.TrySeedAsync(
                _gate.ConfiguredMode, workerName, cancellationToken);

            _logger.LogWarning(
                "Subscription scheduler fleet record {Outcome} ConfiguredMode={ConfiguredMode} " +
                "WorkerName={WorkerName}",
                seeded ? "created" : "was created by another replica",
                _gate.ConfiguredMode,
                PaymentLogValue.Label(workerName));

            await ReportAsync(workerName, SchedulerReplicaState.Drained, cancellationToken);

            return;
        }

        var record = view.Record;

        if (record.Generation != _gate.ActiveGeneration)
        {
            await HandOverAsync(workerName, view, record, cancellationToken);

            return;
        }

        await ReportAsync(workerName, SchedulerReplicaState.Running, cancellationToken);
        await ProposeIfFleetAgreesAsync(workerName, view, record, cancellationToken);
    }

    /// <summary>Drops this replica's row so a planned restart does not hold a mode change up.</summary>
    public async Task WithdrawAsync(string workerName, CancellationToken cancellationToken)
    {
        try
        {
            await _coordinator.RemoveAsync(workerName, cancellationToken);
        }
        catch (Exception exception)
        {
            // Best effort by nature: a pod that is killed cannot do this either, which is what the
            // expiry window is for. Worth a line, never worth failing a shutdown over.
            _logger.LogWarning(
                exception,
                "Subscription scheduler replica row could not be removed on shutdown " +
                "WorkerName={WorkerName}",
                PaymentLogValue.Label(workerName));
        }
    }

    private async Task HandOverAsync(
        string workerName,
        SchedulerFleetView view,
        SubscriptionSchedulerModeRecord record,
        CancellationToken cancellationToken)
    {
        _gate.Close($"mode change to {record.DesiredMode} at generation {record.Generation}");

        var inFlight = _gate.InFlight;

        if (inFlight > 0)
        {
            // Still holding work started in the old mode. Reported at the generation this replica is
            // actually still in, so the rest of the fleet keeps waiting: a replica that claimed the
            // new generation here would be telling the others it is finished when it is not.
            await ReportAsync(workerName, SchedulerReplicaState.Draining, cancellationToken);

            _logger.LogInformation(
                "Draining before a subscription background work mode change " +
                "InFlightCount={InFlightCount} FromGeneration={FromGeneration} " +
                "ToGeneration={ToGeneration}",
                inFlight,
                _gate.ActiveGeneration,
                record.Generation);

            return;
        }

        await ReportAsync(
            workerName, SchedulerReplicaState.Drained, cancellationToken, record.Generation);

        if (!view.MayActivate(record.Generation, workerName))
        {
            _logger.LogInformation(
                "Waiting for the rest of the fleet before running subscription background work in " +
                "{DesiredMode} Generation={Generation} WaitingFor={WaitingFor}",
                record.DesiredMode,
                record.Generation,
                string.Join(",", view.Blockers(record.Generation, workerName)));

            return;
        }

        _gate.Activate(record.DesiredMode, record.Generation);

        await ReportAsync(workerName, SchedulerReplicaState.Running, cancellationToken);
    }

    private async Task ProposeIfFleetAgreesAsync(
        string workerName,
        SchedulerFleetView view,
        SubscriptionSchedulerModeRecord record,
        CancellationToken cancellationToken)
    {
        if (record.DesiredMode == _gate.ConfiguredMode)
        {
            return;
        }

        if (!view.Settled(record.Generation))
        {
            // A change is still being taken up. Proposing another one now would make every replica
            // drain again for a generation none of them has finished reaching.
            return;
        }

        if (!view.UnanimouslyConfiguredFor(_gate.ConfiguredMode))
        {
            // Said once per generation rather than every few seconds: during a rolling deployment
            // this is the normal state of the world for as long as the roll takes.
            if (_announcedProposalGeneration != record.Generation)
            {
                _announcedProposalGeneration = record.Generation;

                _logger.LogWarning(
                    "This replica is configured for {ConfiguredMode} but the fleet is running " +
                    "{DesiredMode}, and will keep running it until every replica's configuration " +
                    "agrees Generation={Generation} DisagreeingReplicas={DisagreeingReplicas}",
                    _gate.ConfiguredMode,
                    record.DesiredMode,
                    record.Generation,
                    string.Join(
                        ",",
                        view.LiveReplicas
                            .Where(replica => replica.ConfiguredMode != _gate.ConfiguredMode)
                            .Select(replica => replica.WorkerName)));
            }

            return;
        }

        var proposed = await _coordinator.TryProposeAsync(
            _gate.ConfiguredMode, record.Generation, workerName, cancellationToken);

        if (proposed)
        {
            _logger.LogWarning(
                "Subscription background work mode change proposed to {DesiredMode} " +
                "Generation={Generation} ProposedBy={ProposedBy}",
                _gate.ConfiguredMode,
                record.Generation + 1,
                PaymentLogValue.Label(workerName));
        }
    }

    private async Task ReportAsync(
        string workerName,
        SchedulerReplicaState state,
        CancellationToken cancellationToken,
        long? generation = null)
    {
        try
        {
            await _coordinator.ReportAsync(
                workerName,
                _gate.ConfiguredMode,
                _gate.ActiveMode,
                generation ?? _gate.ActiveGeneration,
                state,
                cancellationToken);

            _lastReported = _time.GetUtcNow();
            _reportedGeneration = generation ?? _gate.ActiveGeneration;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FenceIfSilentTooLong(exception);
        }
    }

    /// <summary>
    /// Stops this replica once it can no longer prove the fleet can still see it.
    /// </summary>
    private void FenceIfSilentTooLong(Exception exception)
    {
        var silence = _time.GetUtcNow() - _lastReported;

        if (silence < SilenceDeadline)
        {
            // Still visible as far as anyone knows, and a transition cannot complete without this
            // replica's own acknowledgement — so carrying on in the current mode is both safe and
            // the only option that keeps money moving through a database blip.
            _logger.LogWarning(
                exception,
                "Subscription scheduler fleet coordination is unavailable; continuing in " +
                "{ActiveMode} SilenceSeconds={SilenceSeconds} DeadlineSeconds={DeadlineSeconds}",
                _gate.ActiveMode,
                (long)silence.TotalSeconds,
                (long)SilenceDeadline.TotalSeconds);

            return;
        }

        _logger.LogError(
            exception,
            "Subscription scheduler fleet coordination has been unavailable for longer than this " +
            "replica's row survives; stopping background work rather than working unseen " +
            "SilenceSeconds={SilenceSeconds} DeadlineSeconds={DeadlineSeconds} " +
            "ReportedGeneration={ReportedGeneration}",
            (long)silence.TotalSeconds,
            (long)SilenceDeadline.TotalSeconds,
            _reportedGeneration);

        _gate.Close("fleet coordination unreachable for longer than this replica's row survives");
    }
}
