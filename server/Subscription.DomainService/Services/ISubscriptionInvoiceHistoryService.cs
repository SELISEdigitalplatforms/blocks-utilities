using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface ISubscriptionInvoiceHistoryService
{
    Task<SubscriptionOperationResult<SubscriptionInvoiceHistoryResponse>> ListAsync(
        GetSubscriptionInvoicesRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
