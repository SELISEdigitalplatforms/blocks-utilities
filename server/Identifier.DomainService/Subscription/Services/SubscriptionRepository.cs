using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Subscription.Services;
using MongoDB.Driver;

namespace DomainService.Subscription.Services
{
    public class SubscriptionRepository : ISubscriptionRepository
    {
        private readonly IDbContextProvider  _dbContextProvider;

        public SubscriptionRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<List<ResourceLimit>> GetSubscriptionsAsync()
        {
            var collection = _dbContextProvider.GetCollection<ResourceLimit>("ResourceLimits");
            return await collection.Find(FilterDefinition<ResourceLimit>.Empty).ToListAsync();
        }
    }
}
