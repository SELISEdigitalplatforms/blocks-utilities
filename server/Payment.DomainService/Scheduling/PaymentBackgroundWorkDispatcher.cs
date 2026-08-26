using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// Claims due work and runs it. The consumer half of the queue.
/// </summary>
/// <remarks>
/// Claims a bounded batch and runs it with bounded parallelism, because the work moves money
/// through a payment provider: unbounded fan-out across thousands of tenants would replace a
/// latency problem with a rate-limit one.
/// <para>
/// A lease stops two workers running the same item at the same time. It does not make anything
/// exactly-once — a worker can succeed at the provider and die before recording it. What makes that
/// safe is the provider idempotency key each handler's own path derives from persisted state, so a
/// second attempt finds the first charge instead of raising another.
/// </para>
/// </remarks>
public sealed class PaymentBackgroundWorkDispatcher : IPaymentBackgroundWorkDispatcher
{
    private readonly IPaymentWorkQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentBackgroundWorkDispatcher> _logger;
    private readonly PaymentWorkMetrics? _metrics;
    private readonly TimeProvider _time;
    private readonly TimeSpan? _leaseRenewalInterval;
    private readonly TimeSpan? _leaseOverride;

    /// <param name="leaseRenewalInterval">
    /// How often a held lease is renewed. Defaults to half the lease, which is the only sensible
    /// production answer — it leaves a whole interval of slack for a slow renewal before anything
    /// can be taken away. Overridden only by tests, which cannot wait a minute to observe a
    /// renewal that a real handler triggers by taking that long.
    /// </param>
    /// <param name="leaseOverride">
    /// The lease this dispatcher claims work under, in place of the configured one.
    /// </param>
    /// <remarks>
    /// Also for tests, and for one specific reason: the safety deadline is derived from the lease,
    /// so with a production lease it is minutes away. Moving a fake clock to reach it instead makes
    /// the test depend on that write becoming visible to the renewal loop before the loop reads it —
    /// a race that passes alone and fails under load. A short real lease is deterministic.
    /// </remarks>
    public PaymentBackgroundWorkDispatcher(
        IPaymentWorkQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentBackgroundWorkDispatcher> logger,
        TimeProvider? time = null,
        TimeSpan? leaseRenewalInterval = null,
        TimeSpan? leaseOverride = null,
        PaymentWorkMetrics? metrics = null)
    {
        _metrics = metrics;
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _leaseRenewalInterval = leaseRenewalInterval;
        _leaseOverride = leaseOverride;
    }

    public async Task<int> ProcessDueAsync(string workerName, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var leaseId = Guid.NewGuid().ToString("N");
        var lease = _leaseOverride
            ?? TimeSpan.FromSeconds(Math.Max(30, options.SchedulerLeaseSeconds));

        var claimed = await _queue.ClaimDueAsync(
            leaseId,
            workerName,
            Math.Max(1, options.SchedulerBatchSize),
            lease,
            cancellationToken);

        if (claimed.Count == 0)
        {
            return 0;
        }

        foreach (var item in claimed)
        {
            _metrics?.RecordClaimed(item.WorkType);
        }

        var parallelism = Math.Max(1, options.SchedulerMaxParallelism);
        var processed = 0;

        await Parallel.ForEachAsync(
            claimed,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken
            },
            async (work, token) =>
            {
                if (await RunAsync(work, leaseId, lease, token))
                {
                    Interlocked.Increment(ref processed);
                }
            });

        return processed;
    }

    private async Task<bool> RunAsync(
        PaymentBackgroundWork work,
        string leaseId,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        // Everything about this attempt, on every line it writes: the item, who is on it, which
        // attempt, and the correlation the work was created under. One operation has to be
        // traceable from the API call that scheduled it to the provider request that finished it.
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WorkItemId"] = work.ItemId,
            ["WorkType"] = work.WorkType,
            ["WorkKey"] = PaymentLogValue.Label(work.WorkKey),
            // In clear, not hashed. These name records rather than people, and an operator holding
            // a subscription id from the database or the console has to be able to reach its
            // scheduler lines without recomputing a digest — which is the reason PaymentLogValue.Id
            // exists at all.
            ["TenantId"] = PaymentLogValue.Id(work.TenantId),
            ["PaymentId"] = PaymentLogValue.Id(work.AggregateId),
            ["OrganizationId"] = PaymentLogValue.Id(work.OrganizationId ?? string.Empty),
            ["CorrelationId"] = PaymentLogValue.Id(work.CorrelationId),
            ["OperationId"] = work.OperationId,
            ["LeaseId"] = leaseId,
            ["AttemptCount"] = work.AttemptCount
        });

        var startedAt = _time.GetUtcNow().UtcDateTime;
        var options = _options.CurrentValue;

        // Cancelled when the lease is lost as well as when the process stops, so a handler that
        // outlived its claim stops touching money at its next cancellation check rather than
        // running on beside whoever holds the item now.
        using var leaseLost = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var renewal = new LeaseRenewal(
            _queue, work.ItemId, leaseId, lease, _leaseRenewalInterval, leaseLost, _time, _logger);

        try
        {
            var outcome = await ExecuteAsync(work, renewal.Token);
            var duration = _time.GetUtcNow().UtcDateTime - startedAt;

            await renewal.StopAsync();

            if (renewal.LeaseWasLost)
            {
                _metrics?.RecordLeaseLost(work.WorkType);

                // Somebody else owns the item. Not completed and not failed: this attempt has no
                // standing to write either, and saying "completed" here is how a reclaimed item
                // that ran twice looks like one that ran once.
                _logger.LogError(
                    "Payment work finished after losing its lease; the outcome was not " +
                    "recorded and the current holder decides this item Result={Result} " +
                    "DurationMs={DurationMs}",
                    outcome.Result,
                    (long)duration.TotalMilliseconds);

                return false;
            }

            switch (outcome.Result)
            {
                case PaymentWorkResult.Completed:
                    var recorded = await _queue.CompleteAsync(
                        work.ItemId,
                        leaseId,
                        TimeSpan.FromDays(Math.Max(1, options.SchedulerCompletedRetentionDays)),
                        cancellationToken);

                    if (!recorded)
                    {
                        // The lease moved between the last renewal and this write. The work itself
                        // succeeded, so this is not a failure to retry — but it is not this
                        // attempt's completion to claim either.
                        _logger.LogWarning(
                            "Payment work succeeded but its completion was not recorded: the " +
                            "lease is no longer held DurationMs={DurationMs}",
                            (long)duration.TotalMilliseconds);

                        return false;
                    }

                    _metrics?.RecordCompleted(
                        work.WorkType, duration, startedAt - work.DueAtUtc);

                    _logger.LogInformation(
                        "Payment work completed DueAtUtc={DueAtUtc} " +
                        "DurationMs={DurationMs} LagSeconds={LagSeconds}",
                        work.DueAtUtc,
                        (long)duration.TotalMilliseconds,
                        (long)(startedAt - work.DueAtUtc).TotalSeconds);

                    return true;

                default:
                    await FailAsync(work, leaseId, outcome, duration, cancellationToken);

                    return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. The lease expires on its own and another worker picks the item up,
            // which is exactly what the expired-lease path is for.
            throw;
        }
        catch (Exception exception)
        {
            await renewal.StopAsync();

            if (renewal.LeaseWasLost)
            {
                _logger.LogError(
                    exception,
                    "Payment work threw after losing its lease; the current holder decides " +
                    "this item");

                return false;
            }

            await FailAsync(
                work,
                leaseId,
                PaymentWorkOutcome.Retry("unhandled_exception", exception.GetType().Name),
                _time.GetUtcNow().UtcDateTime - startedAt,
                cancellationToken);

            _logger.LogError(exception, "Payment work threw and will be retried");

            return false;
        }
    }

    private async Task<PaymentWorkOutcome> ExecuteAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Background work has no request to read a tenant from, so one is established for the
        // duration â€” the same discipline the reconciliation sweep and the payment consumer follow.
        using var context = services
            .GetRequiredService<IPaymentTenantContextScopeFactory>()
            .Establish(work.TenantId);

        var handler = services
            .GetServices<IPaymentWorkHandler>()
            .FirstOrDefault(candidate => candidate.WorkType == work.WorkType);

        if (handler is null)
        {
            // A work type this build does not know how to run. Dead-lettered rather than retried
            // forever: the next deployment may add it, and an operator can requeue it then.
            return PaymentWorkOutcome.Permanent(
                "work_type_unhandled",
                $"No handler is registered for {work.WorkType}.");
        }

        return await handler.ExecuteAsync(work, cancellationToken);
    }

    private async Task FailAsync(
        PaymentBackgroundWork work,
        string leaseId,
        PaymentWorkOutcome outcome,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var status = await _queue.FailAsync(
            work.ItemId,
            leaseId,
            outcome.ErrorCode ?? "unknown",
            outcome.ErrorMessage ?? string.Empty,
            outcome.Result == PaymentWorkResult.Permanent,
            BackoffFor(work.AttemptCount),
            cancellationToken);

        if (status == BackgroundWorkStatus.DeadLetter)
        {
            _metrics?.RecordDeadLettered(
                work.WorkType, outcome.ErrorCode ?? "unknown", duration);

            // No audit event: this module has no audit trail to write one to. Subscription work
            // records abandonment as a business fact; payment work cannot yet, and the README says
            // so rather than leaving the asymmetry to be discovered.

            // Logged at error because it is the one outcome nothing else will pick up: the work is
            // due, unfinished, and no longer trying.
            _logger.LogError(
                "Payment work dead-lettered and needs a person ErrorCode={ErrorCode} " +
                "ErrorMessage={ErrorMessage} DurationMs={DurationMs}",
                PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"),
                PaymentLogValue.Label(outcome.ErrorMessage ?? string.Empty),
                (long)duration.TotalMilliseconds);

            return;
        }

        _metrics?.RecordRetried(work.WorkType, outcome.ErrorCode ?? "unknown", duration);

        _logger.LogWarning(
            "Payment work failed and will be retried ErrorCode={ErrorCode} " +
            "ErrorMessage={ErrorMessage} DurationMs={DurationMs}",
            PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"),
            PaymentLogValue.Label(outcome.ErrorMessage ?? string.Empty),
            (long)duration.TotalMilliseconds);
    }

    /// <summary>
    /// How long to wait before trying again: exponential, capped, with jitter.
    /// </summary>
    /// <remarks>
    /// Jittered because these items arrive in batches and fail in batches â€” a provider outage would
    /// otherwise have every affected tenant retry in the same second, which is how a recovering
    /// dependency gets knocked over a second time.
    /// </remarks>
    private TimeSpan BackoffFor(int attemptCount)
    {
        var options = _options.CurrentValue;
        var baseSeconds = Math.Max(1, options.SchedulerRetryBaseSeconds);
        var capSeconds = Math.Max(baseSeconds, options.SchedulerRetryMaxSeconds);

        var exponent = Math.Min(Math.Max(0, attemptCount - 1), 16);
        var seconds = Math.Min(capSeconds, baseSeconds * Math.Pow(2, exponent));
        var jitter = seconds * 0.2 * Random.Shared.NextDouble();

        return TimeSpan.FromSeconds(seconds + jitter);
    }

    /// <summary>
    /// Keeps a claimed item's lease alive for as long as the handler is still working on it, and
    /// stops the handler the moment that can no longer be proven.
    /// </summary>
    /// <remarks>
    /// The invariant is one sentence: if ownership cannot be <em>positively</em> renewed before the
    /// safety deadline, the handler stops before another worker can reclaim the item. Everything
    /// below exists to hold that even when the database neither answers nor fails.
    /// <list type="bullet">
    /// <item>Every renewal carries a cancellation token whose deadline is earlier than the lease it
    /// is renewing, so a call cannot outlive the claim it is trying to extend.</item>
    /// <item>An independent watchdog runs beside it. Safety cannot depend on the Mongo call
    /// returning at all — a socket that hangs is not an exception, and the earlier version of this
    /// waited on one forever while the lease quietly expired.</item>
    /// <item>Renewing moves the confirmed expiry forward. Failing does not, and neither does
    /// trying.</item>
    /// </list>
    /// </remarks>
    private sealed class LeaseRenewal : IDisposable
    {
        /// <summary>
        /// How much of the lease is kept in reserve, so a renewal that is going to fail has failed
        /// before the item becomes claimable rather than exactly as it does.
        /// </summary>
        private static readonly TimeSpan DefaultSafetyMargin = TimeSpan.FromSeconds(5);

        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _leaseLost;
        private readonly Task _loop;

        public LeaseRenewal(
            IPaymentWorkQueue queue,
            string itemId,
            string leaseId,
            TimeSpan lease,
            TimeSpan? renewalInterval,
            CancellationTokenSource leaseLost,
            TimeProvider time,
            ILogger logger)
        {
            _leaseLost = leaseLost;
            Token = leaseLost.Token;

            var interval = renewalInterval
                ?? TimeSpan.FromMilliseconds(Math.Max(1_000, lease.TotalMilliseconds / 2));

            // A quarter of the lease at most, so a short lease keeps a proportionate reserve rather
            // than a margin wider than the lease itself.
            var safetyMargin = TimeSpan.FromMilliseconds(
                Math.Min(DefaultSafetyMargin.TotalMilliseconds, lease.TotalMilliseconds / 4));

            _loop = Task.Run(() => RunAsync(
                queue, itemId, leaseId, lease, interval, safetyMargin, time, logger));
        }

        /// <summary>The token a handler runs under: cancelled by shutdown or by a lost lease.</summary>
        public CancellationToken Token { get; }

        public bool LeaseWasLost { get; private set; }

        private async Task RunAsync(
            IPaymentWorkQueue queue,
            string itemId,
            string leaseId,
            TimeSpan lease,
            TimeSpan interval,
            TimeSpan safetyMargin,
            TimeProvider time,
            ILogger logger)
        {
            // What this attempt can prove about its claim. Moved forward only by a renewal that
            // came back true.
            var confirmedUntil = time.GetUtcNow() + lease;

            while (!_stopping.IsCancellationRequested)
            {
                var deadline = confirmedUntil - safetyMargin;

                if (!await WaitBeforeNextAttemptAsync(deadline, interval, time))
                {
                    return;
                }

                var remaining = deadline - time.GetUtcNow();

                if (remaining <= TimeSpan.Zero)
                {
                    await LoseAsync(logger, "the lease expired before it could be renewed", confirmedUntil);

                    return;
                }

                var renewed = await TryRenewAsync(
                    queue, itemId, leaseId, lease, remaining, time, logger, confirmedUntil);

                switch (renewed)
                {
                    case RenewalResult.Renewed:
                        confirmedUntil = time.GetUtcNow() + lease;

                        break;

                    case RenewalResult.Unanswered:
                        // The call neither succeeded nor failed within the deadline. Nothing here
                        // can distinguish a slow database from a lost lease, and only one of those
                        // is safe to assume.
                        await LoseAsync(
                            logger, "the lease could not be renewed before its deadline", confirmedUntil);

                        return;

                    case RenewalResult.Lost:
                        await LoseAsync(logger, "the lease is held by another worker", confirmedUntil);

                        return;

                    default:
                        // Failed outright. The loop re-checks the deadline: while the confirmed
                        // lease still covers this attempt there is room to try again.
                        break;
                }
            }
        }

        /// <summary>
        /// Waits for the next renewal attempt, or until the deadline, whichever comes first.
        /// </summary>
        private async Task<bool> WaitBeforeNextAttemptAsync(
            DateTimeOffset deadline,
            TimeSpan interval,
            TimeProvider time)
        {
            var untilDeadline = deadline - time.GetUtcNow();
            var wait = untilDeadline < interval ? untilDeadline : interval;

            if (wait <= TimeSpan.Zero)
            {
                return true;
            }

            try
            {
                await Task.Delay(wait, time, _stopping.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                // The handler finished, so there is nothing left to renew for.
                return false;
            }
        }

        private async Task<RenewalResult> TryRenewAsync(
            IPaymentWorkQueue queue,
            string itemId,
            string leaseId,
            TimeSpan lease,
            TimeSpan remaining,
            TimeProvider time,
            ILogger logger,
            DateTimeOffset confirmedUntil)
        {
            // Never CancellationToken.None: a renewal that outlives the lease it is renewing is a
            // call whose answer can no longer mean anything.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            attempt.CancelAfter(remaining);

            var renewal = queue.RenewLeaseAsync(itemId, leaseId, lease, attempt.Token);

            // The watchdog is deliberately not the token above. A hung socket may never observe
            // cancellation, and safety cannot wait on a task that never completes.
            var watchdog = Task.Delay(remaining, time, CancellationToken.None);

            if (await Task.WhenAny(renewal, watchdog) != renewal)
            {
                // Abandoned rather than awaited, so a task that completes later cannot fault
                // unobserved — and cannot revive an attempt that has already given up.
                Observe(renewal);

                return RenewalResult.Unanswered;
            }

            try
            {
                return await renewal ? RenewalResult.Renewed : RenewalResult.Lost;
            }
            catch (OperationCanceledException)
            {
                return RenewalResult.Unanswered;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Payment work could not renew its lease and will try again while its " +
                    "confirmed lease lasts ConfirmedUntil={ConfirmedUntil}",
                    confirmedUntil);

                return RenewalResult.Failed;
            }
        }

        private async Task LoseAsync(
            ILogger logger,
            string reason,
            DateTimeOffset confirmedUntil)
        {
            LeaseWasLost = true;

            logger.LogError(
                "Payment work was asked to stop because {Reason} " +
                "ConfirmedUntil={ConfirmedUntil}",
                reason,
                confirmedUntil);

            await _leaseLost.CancelAsync();
        }

        /// <summary>Keeps an abandoned task's failure from surfacing as an unobserved exception.</summary>
        private static void Observe(Task task) =>
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        public async Task StopAsync()
        {
            if (!_stopping.IsCancellationRequested)
            {
                await _stopping.CancelAsync();
            }

            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Stopping the renewal loop is not a failure of the work it was renewing for.
            }
        }

        public void Dispose() => _stopping.Dispose();

        private enum RenewalResult
        {
            Renewed,
            Failed,
            Unanswered,
            Lost
        }
    }
}

public interface IPaymentBackgroundWorkDispatcher
{
    /// <summary>Claims one batch of due work and runs it. Returns how many completed.</summary>
    Task<int> ProcessDueAsync(string workerName, CancellationToken cancellationToken);
}
