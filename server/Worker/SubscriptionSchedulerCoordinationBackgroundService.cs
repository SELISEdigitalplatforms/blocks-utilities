using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Worker;

/// <summary>
/// Keeps this replica's place in the fleet, and moves it when the fleet moves.
/// </summary>
/// <remarks>
/// A thin loop on purpose: every rule lives in <see cref="SubscriptionSchedulerFleetSynchronizer"/>,
/// which can be tested without a database, and this decides only how often to ask and what to do
/// when asking throws.
/// <para>
/// It runs beside the sweep and the queue drainer rather than inside either, because both of them
/// obey the same gate and neither is guaranteed to be running: in direct mode nothing drains, and a
/// replica whose sweep is mid-pass still has to answer for itself.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerCoordinationBackgroundService : BackgroundService
{
    private const int MinimumPollSeconds = 1;

    private readonly SubscriptionSchedulerFleetSynchronizer _synchronizer;
    private readonly ISubscriptionSchedulerCoordinator _coordinator;
    private readonly SubscriptionSchedulerModeGate _gate;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionSchedulerCoordinationBackgroundService> _logger;

    /// <summary>The name this replica is known by in the roster, and in everyone else's logs.</summary>
    private readonly string _workerName = $"{Environment.MachineName}:{Environment.ProcessId}";

    public SubscriptionSchedulerCoordinationBackgroundService(
        SubscriptionSchedulerFleetSynchronizer synchronizer,
        ISubscriptionSchedulerCoordinator coordinator,
        SubscriptionSchedulerModeGate gate,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionSchedulerCoordinationBackgroundService> logger)
    {
        _synchronizer = synchronizer;
        _coordinator = coordinator;
        _gate = gate;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_gate.CoordinationEnabled)
        {
            // Nothing to coordinate with, and nothing written to the root database. The gate is
            // permanently open at the configured mode, which is what this code did before the fleet
            // record existed.
            _logger.LogInformation(
                "Subscription scheduler fleet coordination is disabled; this process runs " +
                "{ConfiguredMode} on its own configuration WorkerName={WorkerName}",
                _gate.ConfiguredMode,
                PaymentLogValue.Label(_workerName));

            return;
        }

        _logger.LogWarning(
            "Subscription scheduler fleet coordination enabled. This replica does no background work " +
            "until the fleet record says which mode is in force ConfiguredMode={ConfiguredMode} " +
            "ReplicaExpirySeconds={ReplicaExpirySeconds} WorkerName={WorkerName}",
            _gate.ConfiguredMode,
            (long)_synchronizer.ReplicaExpiry.TotalSeconds,
            PaymentLogValue.Label(_workerName));

        await EnsureIndexesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _synchronizer.SyncAsync(_workerName, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // The synchronizer already handles an unreachable database, including fencing this
                // replica when it has been silent too long. Anything reaching here is a bug rather
                // than an outage, and ending the loop over it would leave the gate wherever it
                // happened to be with nothing left to move it.
                _logger.LogError(
                    exception,
                    "Subscription scheduler fleet coordination pass failed and will be retried");
            }

            try
            {
                await Task.Delay(PollInterval(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Deliberately not the stopping token: it is already cancelled by the time we get here, and
        // the point of this call is to hand the fleet a clean handover rather than make it wait out
        // the expiry window for a pod that stopped politely.
        using var withdrawal = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _gate.Close("this replica is shutting down");
        await _synchronizer.WithdrawAsync(_workerName, withdrawal.Token);

        _logger.LogInformation(
            "Subscription scheduler fleet coordination stopped WorkerName={WorkerName}",
            PaymentLogValue.Label(_workerName));
    }

    /// <summary>
    /// Creates the roster's indexes, and carries on if it cannot.
    /// </summary>
    /// <remarks>
    /// Not a gate, unlike the work queue's indexes. Nothing here is unique-constrained: the mode
    /// record is a single fixed key and the roster is keyed by worker name, so a missing index costs
    /// a collection scan over a handful of small documents rather than a duplicate nobody can undo.
    /// </remarks>
    private async Task EnsureIndexesAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _coordinator.EnsureIndexesAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Subscription scheduler coordination indexes could not be created; coordination " +
                "will run without them");
        }
    }

    private TimeSpan PollInterval() => TimeSpan.FromSeconds(
        Math.Max(MinimumPollSeconds, _options.Value.SchedulerCoordinationPollSeconds));
}
