using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads the document ledger on a caller's behalf.
/// </summary>
/// <remarks>
/// Replaces the payment-derived invoice history it was built beside. That version answered from
/// <c>PaymentDetails</c> and could only describe what a payment happened to carry — so a trial had no
/// invoice, a credit had no note, and the breakdown was whatever fields the money path had thought to
/// record. The ledger answers from documents that were composed to be read.
/// </remarks>
public interface ISubscriptionFinancialDocumentHistoryService
{
    Task<SubscriptionOperationResult<SubscriptionFinancialDocumentHistoryResponse>> ListAsync(
        GetFinancialDocumentsRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The stored PDF for one document.
    /// </summary>
    /// <remarks>
    /// Fetched through here rather than by handing out a storage URL. A pre-signed URL is a bearer
    /// token for the file, and one that has left the building cannot be revoked by revoking the
    /// caller's access — which is the only lever there is.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionInvoiceDocument>> GetPdfAsync(
        string documentId,
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
