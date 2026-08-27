using Subscription.DomainService.Entities;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// Reads just the document out of an issue result.
/// </summary>
/// <remarks>
/// The issuer returns a typed outcome now, because six decisions used to share one <c>null</c> and
/// the work handler completed its queue item whichever one it was. The tests below are about what a
/// document <em>says</em> rather than about which decision was reached, so they take the document and
/// leave the outcome to the handler's own tests.
/// </remarks>
internal static class IssuerDocumentTestExtensions
{
    public static async Task<SubscriptionFinancialDocument?> IssueDocumentForPaymentAsync(
        this ISubscriptionFinancialDocumentIssuer issuer,
        string tenantId,
        string paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken) =>
        (await issuer.IssueForPaymentAsync(
            tenantId,
            paymentDetailId,
            correlationId,
            cancellationToken)).Document;
}
