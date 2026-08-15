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

    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionResponse>> GetCurrentAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
