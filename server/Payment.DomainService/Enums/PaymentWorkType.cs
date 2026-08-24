namespace Payment.DomainService.Enums;

/// <summary>
/// The four payment repair workflows persisted by the root scheduler.
/// </summary>
/// <remarks>
/// Numbered explicitly and never renumbered: the value is persisted in the root database, so
/// reordering would silently reinterpret every queued item as work of another kind.
/// </remarks>
public enum PaymentWorkType
{
    /// <summary>
    /// Payments whose work command was written and never dispatched, or dispatched and never
    /// answered. The safety net for money that moved while the process that moved it died.
    /// </summary>
    PaymentReconciliation = 0,

    /// <summary>Captures that were authorized and left unsettled.</summary>
    WebhookRecovery = 1,

    /// <summary>Refunds that were accepted and left unsent.</summary>
    ProviderStateRefresh = 2,

    /// <summary>Payment lifecycle events waiting in the outbox.</summary>
    StoredPaymentCleanup = 3
}
