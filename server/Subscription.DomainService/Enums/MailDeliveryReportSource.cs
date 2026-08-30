namespace Subscription.DomainService.Enums;

/// <summary>
/// Which of the two mail publishers a report came from.
/// </summary>
/// <remarks>
/// Named rather than inferred from whichever subject field happens to be populated: a reader
/// filtering the collection wants "every invoice mail" without knowing that an invoice is the one
/// that carries a document number.
/// </remarks>
public enum MailDeliveryReportSource
{
    /// <summary>An invoice, credit note or receipt, from the financial document delivery sweep.</summary>
    FinancialDocument = 0,

    /// <summary>A usage allowance warning, from the lifecycle event consumer.</summary>
    UsageThreshold = 1
}
