namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Whether this process is actually draining the queue, as opposed to trying to.
/// </summary>
/// <remarks>
/// The drainer is the only thing that runs subscription work now, so a drainer that cannot reach the
/// root database is a worker that looks alive and bills nobody. Before, that state had a fallback:
/// the sweep would execute the work itself. Removing the fallback is deliberate &#8212; two executors
/// is how a renewal gets charged twice &#8212; but it means the outage has to be *visible* instead,
/// which is what this is for.
/// <para>
/// Held as live state rather than probed, because the interesting failures are the ones only the
/// drainer sees: indexes it could not create, a claim that keeps throwing. A probe from outside can
/// confirm the database answers; only the loop knows whether work is moving.
/// </para>
/// <para>
/// Written from one loop and read by health checks and metrics on other threads, so every field goes
/// through <see cref="Interlocked"/> or a lock. Nothing here waits on anything.
/// </para>
/// </remarks>
public sealed class SubscriptionQueueReadiness
{
    private readonly object _sync = new();

    private bool _indexesReady;
    private DateTime? _lastClaimAtUtc;
    private DateTime? _unhealthySinceUtc;
    private string _reason = "the drainer has not started yet";
    private int _consecutiveFailures;

    /// <summary>True once the queue's indexes exist and a claim has last succeeded.</summary>
    /// <remarks>
    /// Both conditions, because either alone is misleading. Indexes without a successful claim is a
    /// process that has connected once and may have been failing ever since; a successful claim
    /// without indexes cannot happen, since the queue refuses to be read without them.
    /// </remarks>
    public bool IsReady
    {
        get
        {
            lock (_sync)
            {
                return _indexesReady && _consecutiveFailures == 0;
            }
        }
    }

    public SubscriptionQueueReadinessReport Describe()
    {
        lock (_sync)
        {
            return new SubscriptionQueueReadinessReport(
                _indexesReady && _consecutiveFailures == 0,
                _indexesReady,
                _lastClaimAtUtc,
                _unhealthySinceUtc,
                _consecutiveFailures,
                _reason);
        }
    }

    /// <summary>The queue's indexes exist, so claiming is permitted to begin.</summary>
    public void IndexesReady()
    {
        lock (_sync)
        {
            _indexesReady = true;
        }
    }

    /// <summary>A pass reached the queue and claimed whatever was due, including nothing.</summary>
    /// <remarks>
    /// An empty claim counts. The question this answers is whether the queue is reachable, and a
    /// reachable queue with nothing in it is the healthiest state there is &#8212; treating it as
    /// "no work seen recently, therefore unwell" would page somebody every quiet night.
    /// </remarks>
    public void ClaimSucceeded(DateTime atUtc)
    {
        lock (_sync)
        {
            _lastClaimAtUtc = atUtc;
            _consecutiveFailures = 0;
            _unhealthySinceUtc = null;
            _reason = string.Empty;
        }
    }

    /// <summary>
    /// A pass could not reach the queue. Records since when, so an alert can wait out a blip.
    /// </summary>
    public void Failed(string reason, DateTime atUtc)
    {
        lock (_sync)
        {
            _consecutiveFailures++;
            _unhealthySinceUtc ??= atUtc;
            _reason = reason;
        }
    }
}

/// <summary>
/// What the drainer has managed so far, for a health endpoint and the gauges.
/// </summary>
/// <param name="UnhealthySinceUtc">
/// When the current run of failures began, or null while healthy. An alert wants the duration rather
/// than the count: one failed pass during a failover is not an incident, and ten seconds apart they
/// are the same number.
/// </param>
public sealed record SubscriptionQueueReadinessReport(
    bool IsReady,
    bool IndexesReady,
    DateTime? LastClaimAtUtc,
    DateTime? UnhealthySinceUtc,
    int ConsecutiveFailures,
    string Reason);
