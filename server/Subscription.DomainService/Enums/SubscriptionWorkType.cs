namespace Subscription.DomainService.Enums;

/// <summary>
/// The kinds of subscription background work the scheduler can carry.
/// </summary>
/// <remarks>
/// Numbered explicitly and never renumbered: the value is persisted in the root database, so a
/// reordering would silently reinterpret every queued item as work of another kind.
/// </remarks>
public enum SubscriptionWorkType
{
    /// <summary>Carry confirmed payment outcomes into subscriptions waiting on them.</summary>
    ActivationSettlement = 0,

    /// <summary>Find first charges raised but never recorded, and recover or expire them.</summary>
    ActivationRecovery = 1,

    /// <summary>Resolve settlement reservations whose caller never came back.</summary>
    SettlementReservationRecovery = 2,

    /// <summary>Renewals and dunning retries that are due.</summary>
    Renewal = 3,

    /// <summary>Close usage windows that have ended so they can be rated.</summary>
    UsagePeriodClosure = 4,

    /// <summary>Charge rated usage invoices.</summary>
    UsageInvoiceCharge = 5,

    /// <summary>Publish subscription lifecycle events waiting in the outbox.</summary>
    OutboxPublication = 6,

    /// <summary>
    /// Issue the financial document for a settled charge, refund or trial start.
    /// </summary>
    /// <remarks>
    /// Scheduled after the money and state transitions commit, and separate from them for exactly
    /// that reason: a document that cannot be written must not undo a payment that succeeded. Names
    /// a payment when the producer scheduled it, and nothing when the repair sweep did.
    /// </remarks>
    FinancialDocumentIssue = 7,

    /// <summary>
    /// Render an issued document to PDF, store it, and publish its mail command.
    /// </summary>
    /// <remarks>
    /// Its own kind rather than part of issuing, so a template or storage failure retries the render
    /// without re-entering the code that allocates a document number.
    /// </remarks>
    FinancialDocumentDelivery = 8
}
