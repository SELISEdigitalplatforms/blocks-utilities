namespace Subscription.DomainService.Enums;

/// <summary>
/// Where an issued document stands, financially.
/// </summary>
/// <remarks>
/// Not a workflow. A document is issued once and its money never changes; what moves is whether that
/// money was later given back, which is recorded here so a list can be read without joining every
/// row to its credit notes.
/// <para>
/// Numbered explicitly for the reason <see cref="FinancialDocumentType"/> is.
/// </para>
/// </remarks>
public enum FinancialDocumentStatus
{
    /// <summary>Issued and settled. What a paid invoice and a credit note both sit at.</summary>
    Issued = 0,

    /// <summary>Some of this invoice has been refunded, and a credit note says how much.</summary>
    PartiallyRefunded = 1,

    /// <summary>All of it has.</summary>
    Refunded = 2
}
