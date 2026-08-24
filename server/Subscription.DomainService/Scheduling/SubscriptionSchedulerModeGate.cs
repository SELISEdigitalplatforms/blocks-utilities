using Microsoft.Extensions.Logging;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Whether this process may do background work right now, and in which mode.
/// </summary>
/// <remarks>
/// <see cref="SubscriptionSchedulerMode"/> answers what this process was <em>configured</em> for,
/// once, at startup. This answers what it is allowed to <em>do</em>, which is a different question
/// as soon as there is more than one replica: a pod rolled out with the queue enabled must not start
/// draining while a pod that has not been restarted yet is still executing the same work directly.
/// <para>
/// Both hosted services ask this before every unit of work and hold the ticket while they run it, so
/// a mode change waits for work already started rather than abandoning it. Asking per unit of work
/// is safe here in a way that asking configuration per pass was not: what changes this is the
/// fleet's own agreed generation, and it only advances once every replica has left the previous one.
/// </para>
/// <para>
/// With coordination switched off it is permanently open at the configured mode, which is exactly
/// what the code did before the fleet record existed.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerModeGate
{
    private readonly ILogger<SubscriptionSchedulerModeGate> _logger;
    private readonly object _sync = new();

    private int _inFlight;
    private bool _open;
    private SchedulerRunMode _activeMode;
    private long _activeGeneration;
    private string _closedReason = "starting up";

    public SubscriptionSchedulerModeGate(
        SubscriptionSchedulerMode mode,
        ILogger<SubscriptionSchedulerModeGate> logger)
    {
        ArgumentNullException.ThrowIfNull(mode);

        _logger = logger;
        ConfiguredMode = mode.QueueDriven ? SchedulerRunMode.Queue : SchedulerRunMode.Direct;
        CoordinationEnabled = mode.CoordinationEnabled;
        _activeMode = ConfiguredMode;

        // Closed until the fleet has been asked, and open immediately when nobody is coordinating.
        // A replica that started acting before it knew the fleet's generation would be the exact
        // failure this exists to prevent, so the safe starting state is doing nothing.
        _open = !CoordinationEnabled;
        _activeGeneration = CoordinationEnabled ? -1 : 0;
    }

    /// <summary>What configuration asked for, unchanged for the life of the process.</summary>
    public SchedulerRunMode ConfiguredMode { get; }

    public bool CoordinationEnabled { get; }

    public SchedulerRunMode ActiveMode
    {
        get
        {
            lock (_sync)
            {
                return _activeMode;
            }
        }
    }

    public long ActiveGeneration
    {
        get
        {
            lock (_sync)
            {
                return _activeGeneration;
            }
        }
    }

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _open;
            }
        }
    }

    /// <summary>Units of work started through this gate and not yet finished.</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>
    /// Takes permission to do one unit of work in <paramref name="mode"/>, or nothing.
    /// </summary>
    /// <remarks>
    /// The ticket must be disposed when the work finishes. A mode change does not complete while one
    /// is outstanding, which is what makes a handover a handover rather than an interruption.
    /// </remarks>
    public SchedulerWorkTicket? TryBegin(SchedulerRunMode mode)
    {
        lock (_sync)
        {
            if (!_open || _activeMode != mode)
            {
                return null;
            }

            Interlocked.Increment(ref _inFlight);

            return new SchedulerWorkTicket(this);
        }
    }

    /// <summary>Opens the gate at a generation the fleet has agreed this process may run at.</summary>
    public void Activate(SchedulerRunMode mode, long generation)
    {
        lock (_sync)
        {
            if (_open && _activeMode == mode && _activeGeneration == generation)
            {
                return;
            }

            var wasOpen = _open;
            var previousMode = _activeMode;
            var previousGeneration = _activeGeneration;

            _open = true;
            _activeMode = mode;
            _activeGeneration = generation;
            _closedReason = string.Empty;

            // At warning, like the startup mode line, and for the same reason: which mode a replica
            // is in is the first thing anybody investigating this needs, and inferring it from
            // behaviour is how the question goes unanswered.
            _logger.LogWarning(
                "Subscription background work mode now {Mode} at generation {Generation} " +
                "PreviousMode={PreviousMode} PreviousGeneration={PreviousGeneration} " +
                "WasRunning={WasRunning}",
                mode,
                generation,
                previousMode,
                previousGeneration,
                wasOpen);
        }
    }

    /// <summary>
    /// Stops this process taking new work, without touching what it is already doing.
    /// </summary>
    public void Close(string reason)
    {
        lock (_sync)
        {
            if (!_open && _closedReason == reason)
            {
                return;
            }

            _open = false;
            _closedReason = reason;

            _logger.LogWarning(
                "Subscription background work paused Reason={Reason} Mode={Mode} " +
                "Generation={Generation} InFlightCount={InFlightCount}",
                reason,
                _activeMode,
                _activeGeneration,
                InFlight);
        }
    }

    /// <summary>Why the gate is closed, for the service that has to report it.</summary>
    public string ClosedReason
    {
        get
        {
            lock (_sync)
            {
                return _closedReason;
            }
        }
    }

    private void Finish() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Permission to do one unit of work, held for as long as the work takes.</summary>
    public sealed class SchedulerWorkTicket : IDisposable
    {
        private readonly SubscriptionSchedulerModeGate _gate;
        private bool _finished;

        internal SchedulerWorkTicket(SubscriptionSchedulerModeGate gate) => _gate = gate;

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _gate.Finish();
        }
    }
}
