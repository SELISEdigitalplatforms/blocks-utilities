using DomainService.Entities;

namespace DomainService.Subscription.Services
{
    public interface ISubscriptionRepository
    {
        public Task<List<ResourceLimit>> GetSubscriptionsAsync();
    }
}
