using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>Schedules payment background work. The producer half of the queue.</summary>
public interface IPaymentWorkScheduler
{
    /// <summary>Schedules one occurrence, idempotently by tenant, type, aggregate and key.</summary>
    Task<bool> ScheduleAsync(
        PaymentWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules without letting a failure reach the caller — what a producer at a point of state
    /// change wants, since by then the payment it announces has already been written.
    /// </summary>
    Task<bool> TryScheduleAsync(
        PaymentWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default);
}
