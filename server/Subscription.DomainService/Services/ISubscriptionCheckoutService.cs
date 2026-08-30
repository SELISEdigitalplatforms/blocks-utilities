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

    /// <summary>
    /// Opens a session that stores a card against a subscription that is already running.
    /// </summary>
    /// <remarks>
    /// Not a payment, and it issues no invoice. A trial that started without a card needs one
    /// before its first paid period, and this is how the subscriber provides it — without ending
    /// the trial, shortening it, or charging anything.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionResponse>> StartPaymentMethodSetupAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
