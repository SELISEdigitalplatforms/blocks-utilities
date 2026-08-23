using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

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
public sealed class SubscriptionWorkDispatcher : ISubscriptionWorkDispatcher
{
    private readonly ISubscriptionWorkQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionWorkDispatcher> _logger;
    private readonly TimeProvider _time;

    public SubscriptionWorkDispatcher(
        ISubscriptionWorkQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionWorkDispatcher> logger,
        TimeProvider? time = null)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> ProcessDueAsync(string workerName, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var leaseId = Guid.NewGuid().ToString("N");
        var lease = TimeSpan.FromSeconds(Math.Max(30, options.SchedulerLeaseSeconds));

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
        SubscriptionBackgroundWork work,
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
            ["TenantHash"] = PaymentLogValue.Hash(work.TenantId),
            ["AggregateHash"] = PaymentLogValue.Hash(work.AggregateId),
            ["OrganizationHash"] = PaymentLogValue.Hash(work.OrganizationId ?? string.Empty),
            ["CorrelationId"] = PaymentLogValue.Label(work.CorrelationId),
            ["OperationId"] = work.OperationId,
            ["LeaseId"] = leaseId,
            ["AttemptCount"] = work.AttemptCount
        });

        var startedAt = _time.GetUtcNow().UtcDateTime;
        var options = _options.CurrentValue;

        try
        {
            var outcome = await ExecuteAsync(work, cancellationToken);
            var duration = _time.GetUtcNow().UtcDateTime - startedAt;

            switch (outcome.Result)
            {
                case SubscriptionWorkResult.Completed:
                    await _queue.CompleteAsync(
                        work.ItemId,
                        leaseId,
                        TimeSpan.FromDays(Math.Max(1, options.SchedulerCompletedRetentionDays)),
                        cancellationToken);

                    _logger.LogInformation(
                        "Subscription work completed DueAtUtc={DueAtUtc} " +
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
            await FailAsync(
                work,
                leaseId,
                SubscriptionWorkOutcome.Retry("unhandled_exception", exception.GetType().Name),
                _time.GetUtcNow().UtcDateTime - startedAt,
                cancellationToken);

            _logger.LogError(exception, "Subscription work threw and will be retried");

            return false;
        }
    }

    private async Task<SubscriptionWorkOutcome> ExecuteAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Background work has no request to read a tenant from, so one is established for the
        // duration — the same discipline the reconciliation sweep and the payment consumer follow.
        using var context = services
            .GetRequiredService<IPaymentTenantContextScopeFactory>()
            .Establish(work.TenantId);

        var handler = services
            .GetServices<ISubscriptionWorkHandler>()
            .FirstOrDefault(candidate => candidate.WorkType == work.WorkType);

        if (handler is null)
        {
            // A work type this build does not know how to run. Dead-lettered rather than retried
            // forever: the next deployment may add it, and an operator can requeue it then.
            return SubscriptionWorkOutcome.Permanent(
                "work_type_unhandled",
                $"No handler is registered for {work.WorkType}.");
        }

        return await handler.ExecuteAsync(work, cancellationToken);
    }

    private async Task FailAsync(
        SubscriptionBackgroundWork work,
        string leaseId,
        SubscriptionWorkOutcome outcome,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var status = await _queue.FailAsync(
            work.ItemId,
            leaseId,
            outcome.ErrorCode ?? "unknown",
            outcome.ErrorMessage ?? string.Empty,
            outcome.Result == SubscriptionWorkResult.Permanent,
            BackoffFor(work.AttemptCount),
            cancellationToken);

        if (status == BackgroundWorkStatus.DeadLetter)
        {
            // Logged at error because it is the one outcome nothing else will pick up: the work is
            // due, unfinished, and no longer trying.
            _logger.LogError(
                "Subscription work dead-lettered and needs a person ErrorCode={ErrorCode} " +
                "ErrorMessage={ErrorMessage} DurationMs={DurationMs}",
                PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"),
                PaymentLogValue.Label(outcome.ErrorMessage ?? string.Empty),
                (long)duration.TotalMilliseconds);

            return;
        }

        _logger.LogWarning(
            "Subscription work failed and will be retried ErrorCode={ErrorCode} " +
            "ErrorMessage={ErrorMessage} DurationMs={DurationMs}",
            PaymentLogValue.Label(outcome.ErrorCode ?? "unknown"),
            PaymentLogValue.Label(outcome.ErrorMessage ?? string.Empty),
            (long)duration.TotalMilliseconds);
    }

    /// <summary>
    /// How long to wait before trying again: exponential, capped, with jitter.
    /// </summary>
    /// <remarks>
    /// Jittered because these items arrive in batches and fail in batches — a provider outage would
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
}

public interface ISubscriptionWorkDispatcher
{
    /// <summary>Claims one batch of due work and runs it. Returns how many completed.</summary>
    Task<int> ProcessDueAsync(string workerName, CancellationToken cancellationToken);
}
