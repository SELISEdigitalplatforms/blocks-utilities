using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface ISubscriptionResponseMapper
{
    SubscriptionResponse ToResponse(
        SubscriptionDetail subscription,
        string? checkoutUrl = null);
}
