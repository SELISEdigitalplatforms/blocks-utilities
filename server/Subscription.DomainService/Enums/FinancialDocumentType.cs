namespace Subscription.DomainService.Enums;

/// <summary>
/// What kind of financial document this is.
/// </summary>
/// <remarks>
/// Numbered explicitly and never renumbered: the value is persisted on issued documents, and a
/// reordering would turn somebody's credit note into an invoice.
/// </remarks>
public enum FinancialDocumentType
{
    /// <summary>A charge that was settled. Money moved in.</summary>
    Invoice = 0,

    /// <summary>
    /// A trial that started. Zero total by construction — it states the terms of a period nobody was
    /// charged for, so the subscriber has a document for the entitlement they are using.
    /// </summary>
    TrialInvoice = 1,

    /// <summary>
    /// Value returned or banked: a refund that confirmed, or a downgrade whose unused time became
    /// credit. Always linked to the invoice it adjusts.
    /// </summary>
    CreditNote = 2
}
