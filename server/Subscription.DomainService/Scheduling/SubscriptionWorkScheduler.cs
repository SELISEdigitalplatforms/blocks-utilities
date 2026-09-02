using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
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
                "TenantId={TenantId} SubscriptionId={SubscriptionId} DueAtUtc={DueAtUtc} " +
                "CorrelationId={CorrelationId}",
                workType,
                PaymentLogValue.Label(workKey),
                PaymentLogValue.Id(tenantId),
                // "none" rather than "missing" when the work is tenant-wide: a sweep has no
                // subscription, and saying so is different from having lost one.
                SubscriptionWorkLogValue.AggregateId(aggregateId),
                dueAtUtc,
                PaymentLogValue.Id(correlationId));
        }

        return created;
    }

    public async Task<bool> TryScheduleAsync(
        SubscriptionWorkType workType,
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
                workType,
                tenantId,
                workKey,
                dueAtUtc,
                correlationId,
                aggregateId,
                organizationId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One place decides that a producer's failure is survivable, so no caller has to
            // remember. The work still happens; it is found by the sweep rather than announced.
            _logger.LogError(
                exception,
                "Subscription work could not be scheduled and will be left to the repair sweep " +
                "WorkType={WorkType} WorkKey={WorkKey} TenantId={TenantId}",
                workType,
                PaymentLogValue.Label(workKey),
                PaymentLogValue.Id(tenantId));

            return false;
        }
    }

    public Task ScheduleReservationRecoveryAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(reservation);

        // After the grace window, not now: a reservation that settles normally is long gone before
        // this comes due, and the handler finds nothing to do. Due immediately, it would recover
        // reservations that were never in trouble.
        var grace = Math.Max(1, _options.CurrentValue.SettlementReservationGraceMinutes);

        return TryScheduleAsync(
            SubscriptionWorkType.SettlementReservationRecovery,
            subscription.TenantId,
            // The reservation is the identity the charge is keyed on too, so a second producer or
            // the sweep lands on this same occurrence.
            $"reservation:{reservation.ReservationId}",
            reservation.ReservedAtUtc.AddMinutes(grace),
            correlationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleActivationRecoveryAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var grace = Math.Max(1, _options.CurrentValue.InitialChargeGraceMinutes);

        return TryScheduleAsync(
            SubscriptionWorkType.ActivationRecovery,
            subscription.TenantId,
            // One first charge per subscription, so the subscription is the occurrence.
            $"activation:{subscription.ItemId}",
            _time.GetUtcNow().UtcDateTime.AddMinutes(grace),
            subscription.CorrelationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleUsagePeriodClosureAsync(
        SubscriptionDetail subscription,
        DateTime dueAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return TryScheduleAsync(
            SubscriptionWorkType.UsagePeriodClosure,
            subscription.TenantId,
            // The instant the window ends identifies it, and is what the sweep would find too.
            $"usage-close:{dueAtUtc:yyyyMMddTHHmmssZ}",
            dueAtUtc,
            subscription.CorrelationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleCancellationEffectiveAsync(
        SubscriptionDetail subscription,
        DateTime effectiveAtUtc,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return TryScheduleAsync(
            SubscriptionWorkType.CancellationEffective,
            subscription.TenantId,
            // One schedule per subscription at a time — a later re-schedule (there is none today,
            // since a schedule can only be set once before it is escalated or finalized) would
            // still land on a distinct occurrence because the boundary itself is part of the key.
            $"cancellation-effective:{subscription.ItemId}:{effectiveAtUtc.Ticks}",
            effectiveAtUtc,
            correlationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleUsageInvoiceChargeAsync(
        SubscriptionDetail subscription,
        string periodKey,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return TryScheduleAsync(
            SubscriptionWorkType.UsageInvoiceCharge,
            subscription.TenantId,
            $"usage-charge:{periodKey}",
            // Due now: the invoice exists, and waiting to charge it only delays revenue and the
            // subscriber's own record of what they used.
            _time.GetUtcNow().UtcDateTime,
            correlationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleOutboxPublicationAsync(
        SubscriptionDetail subscription,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(outboxEvent);

        return TryScheduleAsync(
            SubscriptionWorkType.OutboxPublication,
            subscription.TenantId,
            $"outbox:{outboxEvent.EventId}",
            outboxEvent.NextAttemptAtUtc ?? outboxEvent.CreatedAtUtc,
            outboxEvent.CorrelationId,
            subscription.ItemId,
            subscription.OrganizationId,
            cancellationToken);
    }

    public Task ScheduleUsageProjectionRefreshAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // A one-minute bucket in the occurrence key. The unique occurrence index then collapses every
        // failure inside the same minute onto one item, so a Mongo blip affecting a thousand
        // recordings schedules a handful of repairs rather than a thousand. Coarser would delay the
        // repair of a projection that failed just after a bucket was completed.
        var bucket = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

        return TryScheduleAsync(
            SubscriptionWorkType.UsageProjectionRefresh,
            tenantId,
            $"usage-projection:{subscriptionId}:{bucket}",
            now,
            correlationId,
            subscriptionId,
            organizationId,
            cancellationToken);
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
        // Right after renewal: both decide whether entitlement keeps granting, and a subscriber
        // waiting out a cancellation should not have that resolved any later than a renewal would.
        SubscriptionWorkType.CancellationEffective => 35,
        SubscriptionWorkType.ActivationRecovery => 40,
        SubscriptionWorkType.UsageInvoiceCharge => 50,
        SubscriptionWorkType.UsagePeriodClosure => 60,
        SubscriptionWorkType.OutboxPublication => 70,
        // Last, and deliberately so. Nothing about a document affects entitlement or money, and it
        // must never delay a renewal that does.
        SubscriptionWorkType.FinancialDocumentIssue => 80,
        SubscriptionWorkType.FinancialDocumentDelivery => 90,
        // Below every one of those. It repairs a read model that no billing decision reads,
        // and it must never delay work that moves money.
        SubscriptionWorkType.UsageProjectionRefresh => 95,
        _ => 100
    };
}
