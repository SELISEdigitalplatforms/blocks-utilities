using Blocks.Genesis;
using Iam.DomainService.Services;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace Mfa.DomainService.Services
{
    public class MfaManagementRepository : BaseRepository, IMfaManagementRepository
    {
        public MfaManagementRepository(IDbContextProvider dbContextProvider) : base(dbContextProvider)
        {
        }

        public async Task DeleteItemsAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
            await collection.DeleteManyAsync(dataFilters);
        }

        public async Task<IList<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            return await collection.Find(filter).ToListAsync();
        }

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
            await collection.InsertManyAsync(listOfData);
        }

        public async Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            var options = new ReplaceOptions { IsUpsert = true };
            await collection.ReplaceOneAsync(filterExpression, data, options);
        }
    }
}
