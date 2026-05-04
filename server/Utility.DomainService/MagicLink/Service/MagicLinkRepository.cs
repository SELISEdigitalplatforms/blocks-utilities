using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Repository implementation for MagicLink data operations
    /// </summary>
    public class MagicLinkRepository : IMagicLinkRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IConfiguration _configuration;

        public MagicLinkRepository(IDbContextProvider dbContextProvider, IConfiguration configuration)
        {
            _dbContextProvider = dbContextProvider;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets a MongoDB collection using RootTenantId for all MagicLink operations.
        /// Data isolation is achieved through ProjectKey filtering in queries.
        /// </summary>
        private IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            //var tenantId = _configuration["RootTenantId"];
            var tenantId = "f080a1bea04280a72149fd689d50a48c";
            return _dbContextProvider.GetCollection<T>(tenantId, collectionName);
        }

        #region MagicLink Operations

        public async Task<Models.MagicLink?> GetMagicLinkAsync(string itemId, string? projectKey = null)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filter = filterBuilder.Eq(x => x.ItemId, itemId);

            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<string> CreateMagicLinkAsync(Models.MagicLink link)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            await collection.InsertOneAsync(link);
            return link.ItemId;
        }

        public async Task<bool> UpdateMagicLinkAsync(Models.MagicLink link)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, link.ItemId);
            link.UpdatedAt = DateTime.UtcNow;
            var result = await collection.ReplaceOneAsync(filter, link);
            return result.ModifiedCount > 0;
        }

        public async Task<List<Models.MagicLink>> GetMagicLinksByIdsAsync(List<string> itemIds, string projectKey)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filter = filterBuilder.In(x => x.ItemId, itemIds);

            // Filter by ProjectKey to ensure project-level data isolation
            if (!string.IsNullOrEmpty(projectKey))
            {
                filter &= filterBuilder.Eq(x => x.ProjectKey, projectKey);
            }

            return await collection.Find(filter).ToListAsync();
        }

        public async Task<(List<Models.MagicLink> links, int totalCount)> GetMagicLinksAsync(GetMagicLinksRequest request)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filterBuilder = Builders<Models.MagicLink>.Filter;
            var filters = new List<FilterDefinition<Models.MagicLink>>();

            // Required: Filter by ProjectKey to ensure project-level data isolation
            filters.Add(filterBuilder.Eq(x => x.ProjectKey, request.ProjectKey));

            // Optional: Filter by Type
            if (request.Type.HasValue)
            {
                filters.Add(filterBuilder.Eq(x => x.Type, request.Type.Value));
            }

            // Optional: Search filter for Name and Uri
            if (!string.IsNullOrEmpty(request.SearchText))
            {
                var searchFilter = filterBuilder.Or(
                    filterBuilder.Regex("Name", new BsonRegularExpression($".*{request.SearchText}.*", "i")),
                    filterBuilder.Regex("Uri", new BsonRegularExpression($".*{request.SearchText}.*", "i"))
                );
                filters.Add(searchFilter);
            }

            // Optional: Filter by RequestMethod (for Action type)
            if (!string.IsNullOrEmpty(request.RequestMethod))
            {
                filters.Add(filterBuilder.Eq(x => x.RequestMethod, request.RequestMethod.ToUpperInvariant()));
            }

            // Optional: Filter by ExpiryDate range
            var expiryDateFilters = BuildExpiryDateFilter(request, filterBuilder);
            filters.AddRange(expiryDateFilters);

            // Optional: Filter by Status
            var statusFilter = BuildStatusFilter(request, filterBuilder);
            if (statusFilter != null)
            {
                filters.Add(statusFilter);
            }

            // Combine all filters
            var combinedFilter = filterBuilder.And(filters);

            var totalCount = (int)await collection.CountDocumentsAsync(combinedFilter);
            var links = await collection.Find(combinedFilter)
                .Sort(Builders<Models.MagicLink>.Sort.Descending(x => x.CreatedAt))
                .Skip(request.PageNumber * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            return (links, totalCount);
        }

        public async Task<Models.MagicLink?> IncrementUsageCountAsync(string linkId)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, linkId);
            var update = Builders<Models.MagicLink>.Update
                .Inc(x => x.UsageCount, 1)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var options = new FindOneAndUpdateOptions<Models.MagicLink>
            {
                ReturnDocument = ReturnDocument.After
            };

            return await collection.FindOneAndUpdateAsync(filter, update, options);
        }

        public async Task<bool> MarkAsExpiredAsync(string linkId, MagicLinkExpiredReason reason)
        {
            var collection = GetCollection<Models.MagicLink>(Utilities.MagicLinkConstants.MagicLinksCollection);
            var filter = Builders<Models.MagicLink>.Filter.Eq(x => x.ItemId, linkId);
            var update = Builders<Models.MagicLink>.Update
                .Set(x => x.IsExpired, true)
                .Set(x => x.ExpiredReason, reason.ToString())
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        #endregion

        #region ClientCredentials and Config Operations

        public async Task<ClientCredential?> GetClientCredentialsAsync(string clientCredentialId, string projectKey)
        {
            var database = _dbContextProvider.GetDatabase(projectKey);
            var collection = database.GetCollection<ClientCredential>(Utilities.MagicLinkConstants.ClientCredentialsCollection);
            var filter = Builders<ClientCredential>.Filter.Eq(x => x.ItemId, clientCredentialId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<LinkBasedActionConfig?> GetLinkConfigAsync(string configId, string projectKey)
        {
            var database = _dbContextProvider.GetDatabase(projectKey);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.MagicLinkConstants.LinkBasedActionConfigsCollection);
            var filter = Builders<LinkBasedActionConfig>.Filter.Eq(x => x.ItemId, configId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Builds filter for ExpiryDate range
        /// </summary>
        private static List<FilterDefinition<Models.MagicLink>> BuildExpiryDateFilter(GetMagicLinksRequest request, FilterDefinitionBuilder<Models.MagicLink> filterBuilder)
        {
            var dateFilters = new List<FilterDefinition<Models.MagicLink>>();

            if (request.ExpiryDateRange == null)
            {
                return dateFilters;
            }

            var hasStartDate = request.ExpiryDateRange.StartDate != default(DateTime) && request.ExpiryDateRange.StartDate != null;
            var hasEndDate = request.ExpiryDateRange.EndDate != default(DateTime) && request.ExpiryDateRange.EndDate != null;

            if (hasStartDate && !hasEndDate)
            {
                dateFilters.Add(filterBuilder.Gte(x => x.ExpiryDate, request.ExpiryDateRange.StartDate));
            }
            else if (!hasStartDate && hasEndDate)
            {
                dateFilters.Add(filterBuilder.Lte(x => x.ExpiryDate, request.ExpiryDateRange.EndDate));
            }
            else if (hasStartDate && hasEndDate)
            {
                dateFilters.Add(filterBuilder.And(
                    filterBuilder.Gte(x => x.ExpiryDate, request.ExpiryDateRange.StartDate),
                    filterBuilder.Lte(x => x.ExpiryDate, request.ExpiryDateRange.EndDate)
                ));
            }

            return dateFilters;
        }

        /// <summary>
        /// Builds filter for Status based on business rules.
        /// </summary>
        private static FilterDefinition<Models.MagicLink>? BuildStatusFilter(GetMagicLinksRequest request, FilterDefinitionBuilder<Models.MagicLink> filterBuilder)
        {
            if (string.IsNullOrEmpty(request.Status))
            {
                return null;
            }

            var now = DateTime.UtcNow;

            return request.Status switch
            {
                "ManuallyDisabled" => filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.ManuallyDisabled.ToString()),

                "UsageLimitExceeded" => filterBuilder.Or(
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.UsageLimitExceeded.ToString()),
                    filterBuilder.And(
                        filterBuilder.Gt(x => x.UsageLimit, 0),
                        filterBuilder.Where(x => x.UsageCount >= x.UsageLimit)
                    )
                ),

                "TimeExpired" or "LifespanExpired" => filterBuilder.Or(
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.TimeExpired.ToString()),
                    filterBuilder.Eq(x => x.ExpiredReason, MagicLinkExpiredReason.LifespanExpired.ToString()),
                    filterBuilder.And(
                        filterBuilder.Ne(x => x.ExpiryDate, null),
                        filterBuilder.Lt(x => x.ExpiryDate, now),
                        filterBuilder.Eq(x => x.IsExpired, false)
                    )
                ),

                "Active" => filterBuilder.And(
                    filterBuilder.Eq(x => x.IsExpired, false),
                    filterBuilder.Or(
                        filterBuilder.Eq(x => x.UsageLimit, 0),
                        filterBuilder.Where(x => x.UsageCount < x.UsageLimit)
                    ),
                    filterBuilder.Or(
                        filterBuilder.Eq(x => x.ExpiryDate, null),
                        filterBuilder.Gte(x => x.ExpiryDate, now)
                    )
                ),

                _ => null
            };
        }

        #endregion

        #region Visitor Usage Operations

        public async Task CreateVisitorUsageAsync(MagicLinkVisitorUsage visitorUsage)
        {
            var database = _dbContextProvider.GetDatabase(visitorUsage.ProjectKey);
            var collection = database.GetCollection<MagicLinkVisitorUsage>(Utilities.MagicLinkConstants.MagicLinkVisitorUsagesCollection);
            await collection.InsertOneAsync(visitorUsage);
        }

        #endregion

        #region LinkBasedActionConfig Operations

        public async Task<LinkBasedActionConfig?> GetLinkBasedActionConfigAsync(string projectKey)
        {
            var database = _dbContextProvider.GetDatabase(projectKey);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.MagicLinkConstants.LinkBasedActionConfigsCollection);
            var filter = Builders<LinkBasedActionConfig>.Filter.Eq(x => x.ProjectKey, projectKey);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<string> CreateLinkBasedActionConfigAsync(LinkBasedActionConfig config)
        {
            var database = _dbContextProvider.GetDatabase(config.ProjectKey);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.MagicLinkConstants.LinkBasedActionConfigsCollection);
            await collection.InsertOneAsync(config);
            return config.ItemId;
        }

        public async Task<bool> UpdateLinkBasedActionConfigAsync(LinkBasedActionConfig config)
        {
            var database = _dbContextProvider.GetDatabase(config.ProjectKey);
            var collection = database.GetCollection<LinkBasedActionConfig>(Utilities.MagicLinkConstants.LinkBasedActionConfigsCollection);
            var filter = Builders<LinkBasedActionConfig>.Filter.Eq(x => x.ItemId, config.ItemId);
            config.UpdatedAt = DateTime.UtcNow;
            var result = await collection.ReplaceOneAsync(filter, config);
            return result.ModifiedCount > 0;
        }

        #endregion
    }
}

