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

    /// <summary>
    /// Asks for a document's mail to be sent once more.
    /// </summary>
    /// <remarks>
    /// The counterpart of sending at most once. Nothing automatic will send a mail whose outcome is
    /// unknown — a broker can accept a message and lose the acknowledgement, so a failed publish is not
    /// evidence of non-delivery and retrying it risks a second invoice in somebody's inbox. That leaves
    /// a person to decide, and this is what they decide with.
    /// <para>
    /// Console only, and deliberately so: whoever calls this is accepting that the subscriber may
    /// receive the invoice twice. That is a judgement, not a retry policy.
    /// </para>
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionFinancialDocumentResendResponse>> ResendAsync(
        string documentId,
        string correlationId,
        CancellationToken cancellationToken);
}
