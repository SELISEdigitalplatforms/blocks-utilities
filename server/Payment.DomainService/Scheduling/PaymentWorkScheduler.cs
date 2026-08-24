using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// Schedules payment work, and decides what a kind of work is worth relative to the rest.
/// </summary>
public sealed class PaymentWorkScheduler : IPaymentWorkScheduler
{
    private readonly IPaymentWorkQueue _queue;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentWorkScheduler> _logger;
    private readonly TimeProvider _time;

    public PaymentWorkScheduler(
        IPaymentWorkQueue queue,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentWorkScheduler> logger,
        TimeProvider? time = null)
    {
        _queue = queue;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> ScheduleAsync(
        PaymentWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var created = await _queue.ScheduleAsync(
            new PaymentBackgroundWork
            {
                TenantId = tenantId,
                OrganizationId = organizationId,
                AggregateId = aggregateId,
                WorkType = workType,
                WorkKey = workKey,
                DueAtUtc = dueAtUtc,
                NextAttemptAtUtc = dueAtUtc,
                Priority = PriorityOf(workType),
                MaxAttempts = Math.Max(1, _options.CurrentValue.SchedulerMaxAttempts),
                CorrelationId = correlationId
            },
            cancellationToken);

        if (created)
        {
            _logger.LogInformation(
                "Scheduled payment work WorkType={WorkType} WorkKey={WorkKey} TenantId={TenantId} " +
                "AggregateId={AggregateId} DueAtUtc={DueAtUtc} CorrelationId={CorrelationId}",
                workType,
                PaymentLogValue.Label(workKey),
                // In clear so they can be searched for: PaymentLogValue.Id exists because hashing
                // system identifiers is what made these logs unfollowable.
                PaymentLogValue.Id(tenantId),
                PaymentLogValue.Id(aggregateId),
                dueAtUtc,
                PaymentLogValue.Id(correlationId));
        }

        return created;
    }

    public async Task<bool> TryScheduleAsync(
        PaymentWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ScheduleAsync(
                workType, tenantId, workKey, dueAtUtc, correlationId, aggregateId,
                organizationId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One place decides that a producer's failure is survivable. By the time a producer
            // runs, the payment it is announcing has already been written.
            _logger.LogError(
                exception,
                "Payment work could not be scheduled and will be left to the next pass " +
                "WorkType={WorkType} WorkKey={WorkKey} TenantId={TenantId}",
                workType,
                PaymentLogValue.Label(workKey),
                PaymentLogValue.Id(tenantId));

            return false;
        }
    }

    /// <summary>
    /// What runs first when the queue is behind.
    /// </summary>
    /// <remarks>
    /// Money that has moved outranks events about money. A payment whose provider call succeeded and
    /// whose local write was lost is a customer charged for something the platform does not know it
    /// sold; an outbox event that waits is a notification that arrives late. Captures and refunds sit
    /// between: both are money owed in one direction or the other, and neither is as urgent as a
    /// payment nobody has reconciled.
    /// </remarks>
    private static int PriorityOf(PaymentWorkType workType) => workType switch
    {
        PaymentWorkType.PaymentReconciliation => 10,
        PaymentWorkType.WebhookRecovery => 20,
        PaymentWorkType.ProviderStateRefresh => 30,
        PaymentWorkType.StoredPaymentCleanup => 40,
        _ => 100
    };
}
