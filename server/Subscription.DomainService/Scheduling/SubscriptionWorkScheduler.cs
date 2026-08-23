using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Schedules work, and decides what a kind of work is worth relative to the rest.
/// </summary>
public sealed class SubscriptionWorkScheduler : ISubscriptionWorkScheduler
{
    private readonly ISubscriptionWorkQueue _queue;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionWorkScheduler> _logger;
    private readonly TimeProvider _time;

    public SubscriptionWorkScheduler(
        ISubscriptionWorkQueue queue,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionWorkScheduler> logger,
        TimeProvider? time = null)
    {
        _queue = queue;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<bool> ScheduleAsync(
        SubscriptionWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var created = await _queue.ScheduleAsync(
            new SubscriptionBackgroundWork
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
                "Scheduled subscription work WorkType={WorkType} WorkKey={WorkKey} " +
                "TenantHash={TenantHash} AggregateHash={AggregateHash} DueAtUtc={DueAtUtc} " +
                "CorrelationId={CorrelationId}",
                workType,
                PaymentLogValue.Label(workKey),
                PaymentLogValue.Hash(tenantId),
                PaymentLogValue.Hash(aggregateId),
                dueAtUtc,
                PaymentLogValue.Label(correlationId));
        }

        return created;
    }

    /// <summary>
    /// What runs first when the queue is behind.
    /// </summary>
    /// <remarks>
    /// Money before bookkeeping, deliberately. A renewal that waits is revenue not collected and a
    /// subscriber who may lose access; an outbox event that waits is a notification that arrives
    /// late. Settlement recovery outranks everything because until it resolves, a subscriber may
    /// have been charged for units they have not been given — and their subscription cannot renew
    /// or change plan while it is unresolved.
    /// </remarks>
    private static int PriorityOf(SubscriptionWorkType workType) => workType switch
    {
        SubscriptionWorkType.SettlementReservationRecovery => 10,
        SubscriptionWorkType.ActivationSettlement => 20,
        SubscriptionWorkType.Renewal => 30,
        SubscriptionWorkType.ActivationRecovery => 40,
        SubscriptionWorkType.UsageInvoiceCharge => 50,
        SubscriptionWorkType.UsagePeriodClosure => 60,
        SubscriptionWorkType.OutboxPublication => 70,
        _ => 100
    };
}
