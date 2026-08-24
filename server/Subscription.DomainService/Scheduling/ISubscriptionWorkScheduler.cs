using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Schedules subscription background work. The producer half of the queue.
/// </summary>
public interface ISubscriptionWorkScheduler
{
    /// <summary>
    /// Schedules one occurrence, idempotently.
    /// </summary>
    /// <param name="workKey">
    /// Which occurrence this is — a period key, a time bucket. Two calls naming the same occurrence
    /// produce one item, which is what keeps a retried producer from creating a second chance to
    /// charge.
    /// </param>
    /// <returns>True when this call created the occurrence.</returns>
    Task<bool> ScheduleAsync(
        SubscriptionWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules work without letting a failure reach the caller.
    /// </summary>
    /// <remarks>
    /// What every producer at a point of state change wants. By the time one of them runs, the
    /// thing being announced has already happened — money has moved, a reservation is written —
    /// and a scheduling write in another database that fails must not undo or fail that. The
    /// repair sweep is what covers the miss.
    /// <para>
    /// Kept beside <see cref="ScheduleAsync"/> rather than replacing it, so a caller that genuinely
    /// needs to know can still be told.
    /// </para>
    /// </remarks>
    Task<bool> TryScheduleAsync(
        SubscriptionWorkType workType,
        string tenantId,
        string workKey,
        DateTime dueAtUtc,
        string correlationId,
        string aggregateId = "",
        string? organizationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces the recovery of one settlement reservation.
    /// </summary>
    /// <remarks>
    /// The <em>when</em> lives here rather than at each call site: the grace window is a scheduling
    /// policy, and two services take reservations. Read from configuration in one place, it cannot
    /// drift between them or from the sweep that reads the same setting.
    /// </remarks>
    Task ScheduleReservationRecoveryAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces the recovery of a checkout that may never be paid.
    /// </summary>
    /// <remarks>
    /// Due after the initial-charge grace window, which is how long a shopper is given to finish
    /// paying before the attempt is treated as abandoned. Keyed on the subscription, because there
    /// is exactly one first charge per subscription.
    /// </remarks>
    Task ScheduleActivationRecoveryAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces the closing of a usage window, and the charging of what it rated.
    /// </summary>
    /// <remarks>
    /// Two occurrences from one event, because they are two different kinds of due: a window closes
    /// on the clock, and the invoice it produced is charged as soon as there is one. Keyed on the
    /// usage period either way, so a closure that happens twice — sweep and producer — produces one
    /// of each.
    /// </remarks>
    Task ScheduleUsagePeriodClosureAsync(
        SubscriptionDetail subscription,
        DateTime dueAtUtc,
        CancellationToken cancellationToken = default);

    Task ScheduleUsageInvoiceChargeAsync(
        SubscriptionDetail subscription,
        string periodKey,
        string correlationId,
        CancellationToken cancellationToken = default);
}
