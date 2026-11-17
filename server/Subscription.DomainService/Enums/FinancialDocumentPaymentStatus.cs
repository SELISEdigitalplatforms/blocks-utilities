namespace Subscription.DomainService.Enums;

/// <summary>
/// Whether the money on a document has been received. <c>Paid</c> is the default so documents
/// written before the field existed read as Paid (they were all card-backed).
/// </summary>
public enum FinancialDocumentPaymentStatus
{
    Paid = 0,
    Unpaid = 1
}
