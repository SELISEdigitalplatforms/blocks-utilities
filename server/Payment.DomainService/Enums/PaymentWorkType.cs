namespace Payment.DomainService.Enums;

/// <summary>
/// The kinds of payment background work the scheduler can carry.
/// </summary>
/// <remarks>
/// Numbered explicitly and never renumbered: the value is persisted in the root database, so
/// reordering would silently reinterpret every queued item as work of another kind.
/// <para>
/// One per processor that already exists. The work types named in the ticket that have no processor
/// behind them — provider-state refresh, stored-payment cleanup — are absent on purpose: scheduling
/// work nothing knows how to do would be a queue of items that can only dead-letter.
/// </para>
/// </remarks>
public enum PaymentWorkType
{
    /// <summary>
    /// Payments whose work command was written and never dispatched, or dispatched and never
    /// answered. The safety net for money that moved while the process that moved it died.
    /// </summary>
    PaymentRecovery = 0,

    /// <summary>Captures that were authorized and left unsettled.</summary>
    CaptureRecovery = 1,

    /// <summary>Refunds that were accepted and left unsent.</summary>
    RefundRecovery = 2,

    /// <summary>Payment lifecycle events waiting in the outbox.</summary>
    OutboxPublication = 3,

    /// <summary>Refund lifecycle events waiting in their own outbox.</summary>
    RefundOutboxPublication = 4
}
