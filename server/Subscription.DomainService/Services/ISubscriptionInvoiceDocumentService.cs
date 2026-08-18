namespace Subscription.DomainService.Services;

/// <summary>
/// Reads the invoice document behind a subscription payment.
/// </summary>
public interface ISubscriptionInvoiceDocumentService
{
    /// <param name="paymentId">
    /// The payment recorded for a settled subscription period — not the provider's invoice id,
    /// which is an internal detail a caller is never given.
    /// </param>
    /// <param name="requestedOrganizationId">
    /// An organization named by the request. Honoured only for the console, on the same policy as
    /// every other subscription read.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionInvoiceDocument>> GetAsync(
        string paymentId,
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>An invoice rendered for download.</summary>
public sealed record SubscriptionInvoiceDocument(
    byte[] Content,
    string ContentType,
    string FileName);
