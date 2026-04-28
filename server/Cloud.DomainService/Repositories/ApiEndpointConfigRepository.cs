using Blocks.Genesis;
using Cloud.DomainService.Models;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Responses;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace Cloud.DomainService.Repositories
{
    public class ApiEndpointConfigRepository : IApiEndpointConfigRepository
    {
        private const string CollectionName = "Permissions";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public ApiEndpointConfigRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async Task<(List<ApiEndpointConfigResponse>, long)> GetListAsync(GetApiEndpointConfigsRequest request)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(request.Filter?.ResourceGroup))
                filter &= Builders<ApiEndpointConfig>.Filter.Eq(x => x.ResourceGroup, request.Filter.ResourceGroup);

            if (!string.IsNullOrWhiteSpace(request.Filter?.Controller) && !string.IsNullOrWhiteSpace(request.Filter?.Method))
            {
                filter &= Builders<ApiEndpointConfig>.Filter.Regex(x => x.Resource,
                    new BsonRegularExpression($"^[^:]+::{Regex.Escape(request.Filter.Controller)}::{Regex.Escape(request.Filter.Method)}$", "i"));
            }
            else if (!string.IsNullOrWhiteSpace(request.Filter?.Controller))
            {
                filter &= Builders<ApiEndpointConfig>.Filter.Regex(x => x.Resource,
                    new BsonRegularExpression($"^[^:]+::{Regex.Escape(request.Filter.Controller)}::", "i"));
            }
            else if (!string.IsNullOrWhiteSpace(request.Filter?.Method))
            {
                filter &= Builders<ApiEndpointConfig>.Filter.Regex(x => x.Resource,
                    new BsonRegularExpression($"::{Regex.Escape(request.Filter.Method)}$", "i"));
            }

            var data = await collection.Find(filter)
                .Skip(request.Page * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            var count = await collection.CountDocumentsAsync(filter);
            var mapped = data.Select(x =>
            {
                var parts = x.Resource?.Split("::") ?? [];
                return new ApiEndpointConfigResponse
                {
                    Controller = parts.Length > 1 ? parts[1] : string.Empty,
                    Method = parts.Length > 2 ? parts[2] : string.Empty,
                    Service = x.ResourceGroup,
                    Name = x.Name,
                    Type = x.Type,
                    Description = x.Description,
                    Resource = x.Resource,
                    ResourceGroup = x.ResourceGroup,
                    IsBuiltIn = x.IsBuiltIn,
                    IsArchived = x.IsArchived,
                    DependentPermissions = x.DependentPermissions,
                    Roles = x.Roles,
                    UserId = x.UserId,
                    IsCaptchaRequired = x.IsCaptchaRequired,
                    IsMFARequired = x.IsMFARequired,
                    MfaMediaType = x.MfaMediaType,
                    IsAllowed = x.IsAllowed,
                    Limit = x.Limit,
                    Usage = x.Usage,
                    BaseUrl = x.BaseUrl,
                    Version = x.Version,
                    ItemId = x.ItemId
                };
            }).ToList();
            return (mapped, count);
        }

        public async Task<bool> UpdateAsync(string projectKey, string itemId, bool isCaptchaRequired, bool isMfaRequired, string updatedBy)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.Eq(x => x.ItemId, itemId);
            var update = Builders<ApiEndpointConfig>.Update
                .Set(x => x.IsCaptchaRequired, isCaptchaRequired)
                .Set(x => x.IsMFARequired, isMfaRequired)
                .Set(x => x.LastUpdatedBy, updatedBy)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(filter, update);

            return result.ModifiedCount > 0;
        }

        public async Task<long> BulkUpdateAsync(string projectKey, List<string> itemIds, bool isCaptchaRequired, bool isMfaRequired, string updatedBy)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.In(x => x.ItemId, itemIds);
            var update = Builders<ApiEndpointConfig>.Update
                .Set(x => x.IsCaptchaRequired, isCaptchaRequired)
                .Set(x => x.IsMFARequired, isMfaRequired)
                .Set(x => x.LastUpdatedBy, updatedBy)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
        }
    }
}
