using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface ISubscriptionCheckoutService
{
    /// <summary>
    /// Creates a subscription and, when it needs paying for, raises the first charge.
    /// </summary>
    Task<SubscriptionOperationResult<SubscriptionResponse>> SubscribeAsync(
        CreateSubscriptionRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionResponse>> GetCurrentAsync(
        string correlationId,
        CancellationToken cancellationToken);
}
