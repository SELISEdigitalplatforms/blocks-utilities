using Subscription.DomainService.Entities;
using Subscription.DomainService.Requests;

namespace Subscription.DomainService.Services;

public interface ISubscriptionCreationService
{
    /// <summary>
    /// Builds and stores a subscription in its initial state.
    /// </summary>
    /// <remarks>
    /// Stops short of taking money. Whether a charge is needed at all — and how it is raised —
    /// belongs to the checkout service, so this one stays about turning a chosen plan into a
    /// durable record.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
