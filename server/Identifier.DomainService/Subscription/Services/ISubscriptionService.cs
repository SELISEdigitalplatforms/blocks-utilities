using DomainService.Subscription.RequestModel;
using DomainService.Subscription.ResponseModel;

namespace DomainService.Subscription.Services
{
    public interface ISubscriptionService
    {
        public Task<GetSubscriptionsResponse> GetSubscriptionsAsync(GetSubscriptionsRequest request);
    }
}
