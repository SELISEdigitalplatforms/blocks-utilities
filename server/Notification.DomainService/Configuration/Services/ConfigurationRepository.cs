using Blocks.Genesis;
using DomainService.Entities;
using MongoDB.Driver;

namespace DomainService.Configuration.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "NotificationConfigurations";

        public ConfigurationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<NotificationConfiguration> GetByNameAsync(string name)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.Name, name);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<NotificationConfiguration> GetByIdAsync(string id)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, id);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task SaveAsync(NotificationConfiguration configuration)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_collectionName);

            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, configuration.ItemId);

            await collection.ReplaceOneAsync(
                filter,
                configuration,
                new ReplaceOptions { IsUpsert = true }
            );
        }

        public async Task<GetConfigurationsResponse> GetConfigurationsAsync(GetConfigurationsRequest request)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_collectionName);
            var builder = Builders<NotificationConfiguration>.Filter;
            var filter = FilterDefinition<NotificationConfiguration>.Empty;
            var userId = BlocksContext.GetContext()?.UserId;

            var options = new FindOptions<NotificationConfiguration>
            {
                Skip = request.PageSize * request.Page,
                Limit = request.PageSize,
                Sort = Builders<NotificationConfiguration>.Sort.Descending(n => n.CreatedDate)
            };

            var configurations = await (await collection.FindAsync(filter, options)).ToListAsync();
            var totalCount = await collection.CountDocumentsAsync(_ => true);

            return new GetConfigurationsResponse
            {
                Configurations = configurations,
                TotalCount = totalCount,
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> DeleteConfigurationAsync(DeleteConfigurationRequest request)
        {
            var collection = _dbContextProvider.GetCollection<NotificationConfiguration>(_collectionName);
            var filter = Builders<NotificationConfiguration>.Filter.Eq(mc => mc.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
            
            return new BaseResponse { IsSuccess = true };
        }
    }
}
