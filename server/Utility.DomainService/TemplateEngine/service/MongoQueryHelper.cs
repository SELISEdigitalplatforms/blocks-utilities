using System.Text.RegularExpressions;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Utility.DomainService.TemplateEngine.service
{
    /// <summary>
    /// Helper service for executing MongoDB queries for template rendering
    /// Replaces the old PDS (Platform Data Service) query pattern
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class MongoQueryHelper
    {
        private static readonly Regex EntityNamePattern = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ForbiddenOperators = new(StringComparer.Ordinal)
        {
            "$where", "$function", "$accumulator", "$expr"
        };

        // Collections owned by IAM, Genesis and this service's own modules
        private static readonly HashSet<string> DeniedCollections = new(StringComparer.OrdinalIgnoreCase)
        {
            "Users", "Roles", "Permissions", "ClientCredentials", "Secrets", "Tenants",
            "LinkBasedActionConfigs", "KeyValueStores", "IdentityConfigurations", "AllowedApis",
            "ResourceLimits", "JwtThirdPartyProviders"
        };

        private static readonly string[] DeniedCollectionPrefixes =
        {
            "Subscription", "Payment", "StoredPaymentMethod", "Sms", "MagicLink",
            "DocumentConversion", "PdfIngestion", "PdfSignatureValidation", "PdfGeneration",
            "PdfUtility", "PdfExtract"
        };

        private readonly ILogger<MongoQueryHelper> _logger;
        private readonly IDbContextProvider _dbContextProvider;

        public MongoQueryHelper(
            ILogger<MongoQueryHelper> logger,
            IDbContextProvider dbContextProvider)
        {
            _logger = logger;
            _dbContextProvider = dbContextProvider;
        }

        /// <summary>
        /// Gets entity list from MongoDB query data
        /// </summary>
        public async Task<Dictionary<string, List<object>>> GetEntityListFromQueryData(
            List<FilteredMongoQueryData> filteredQueryDatas, 
            string tenantId)
        {
            _logger.LogInformation("MongoQueryHelper: GetEntityListFromQueryData START");
            var entityList = new Dictionary<string, List<object>>();

            foreach (var queryData in filteredQueryDatas)
            {
                try
                {
                    _logger.LogInformation("MongoQueryHelper: Processing query for entity '{EntityName}'", queryData.EntityName);

                    List<object> response;
                    if (queryData.FetchAllMatchedItem)
                    {
                        response = await GetAllMatchedDataWithoutPagination(queryData, tenantId);
                    }
                    else
                    {
                        response = await GetDataByMongoQuery(queryData, tenantId);
                    }

                    var entityName = queryData.EntityName;
                    var keyName = string.IsNullOrEmpty(queryData.Key) ? $"{entityName}List" : queryData.Key;

                    if (!entityList.ContainsKey(keyName))
                    {
                        entityList[keyName] = new List<object>();
                    }

                    _logger.LogInformation("MongoQueryHelper: Got {Count} items for entity '{EntityName}'", response.Count, entityName);
                    
                    foreach (var objectItem in response)
                    {
                        entityList[keyName].Add(objectItem);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MongoQueryHelper: Error processing query for entity '{EntityName}'", queryData.EntityName);
                }
            }

            _logger.LogInformation("MongoQueryHelper: GetEntityListFromQueryData END, found {EntityListCount} entity lists", entityList.Count);
            return entityList;
        }

        /// <summary>
        /// Executes a MongoDB query with pagination
        /// </summary>
        private async Task<List<object>> GetDataByMongoQuery(FilteredMongoQueryData queryData, string tenantId)
        {
            _logger.LogInformation("MongoQueryHelper: GetDataByMongoQuery for entity '{EntityName}'", queryData.EntityName);

            try
            {
                var (collection, filter, sortDefinition) = GetMongoQueryComponents(queryData, tenantId);

                // Execute query with pagination
                var pageNumber = queryData.PageNumber ?? 0;
                var pageLimit = queryData.PageLimit ?? 100;

                var documents = await collection.Find(filter)
                    .Sort(sortDefinition)
                    .Skip(pageNumber * pageLimit)
                    .Limit(pageLimit)
                    .ToListAsync();

                return ConvertBsonDocumentsToObjects(documents, queryData.EntityName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MongoQueryHelper: Error executing query for entity '{EntityName}'", queryData.EntityName);
                return new List<object>();
            }
        }

        /// <summary>
        /// Fetches all matched data without pagination
        /// </summary>
        private async Task<List<object>> GetAllMatchedDataWithoutPagination(FilteredMongoQueryData queryData, string tenantId)
        {
            _logger.LogInformation("MongoQueryHelper: GetAllMatchedDataWithoutPagination for entity '{EntityName}'", queryData.EntityName);

            try
            {
                var (collection, filter, sortDefinition) = GetMongoQueryComponents(queryData, tenantId);

                // Execute query without pagination
                var documents = await collection.Find(filter)
                    .Sort(sortDefinition)
                    .ToListAsync();

                return ConvertBsonDocumentsToObjects(documents, queryData.EntityName, " (no pagination)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MongoQueryHelper: Error executing query for entity '{EntityName}'", queryData.EntityName);
                return new List<object>();
            }
        }

        /// <summary>
        /// Gets MongoDB collection, filter, and sort definition for a query
        /// </summary>
        private (IMongoCollection<BsonDocument> collection, FilterDefinition<BsonDocument> filter, SortDefinition<BsonDocument> sortDefinition)
            GetMongoQueryComponents(FilteredMongoQueryData queryData, string tenantId)
        {
            // Both checks run before the database is touched
            EnsureEntityIsQueryable(queryData.EntityName);
            var filter = BuildMongoFilter(queryData.Text);

            var database = _dbContextProvider.GetDatabase(tenantId);
            var collectionName = $"{queryData.EntityName}s";
            var collection = database.GetCollection<BsonDocument>(collectionName);

            // Apply sorting
            var sortDefinition = queryData.SortOrder == SortOrder.Descending
                ? Builders<BsonDocument>.Sort.Descending(queryData.OrderBy)
                : Builders<BsonDocument>.Sort.Ascending(queryData.OrderBy);

            return (collection, filter, sortDefinition);
        }

        /// <summary>
        /// Converts BsonDocuments to a list of objects (dictionaries)
        /// </summary>
        private List<object> ConvertBsonDocumentsToObjects(List<BsonDocument> documents, string entityName, string suffix = "")
        {
            _logger.LogInformation("MongoQueryHelper: Found {DocumentCount} documents for entity '{EntityName}'{Suffix}", documents.Count, entityName, suffix);

            var results = new List<object>();
            foreach (var doc in documents)
            {
                var json = doc.ToJson();
                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (obj != null)
                {
                    results.Add(obj);
                }
            }

            return results;
        }

        /// <summary>
        /// Builds a MongoDB filter from a MongoDB JSON query string
        /// Accepts standard MongoDB query JSON format
        /// </summary>
        /// <param name="queryText">MongoDB query in JSON format (e.g., {"status": "completed", "amount": {"$gt": 100}})</param>
        /// <returns>MongoDB FilterDefinition</returns>
        /// <exception cref="ArgumentException">Thrown when query text is invalid</exception>
        private FilterDefinition<BsonDocument> BuildMongoFilter(string queryText)
        {
            // Handle empty or null query text
            if (string.IsNullOrWhiteSpace(queryText))
            {
                _logger.LogInformation("BuildMongoFilter: Empty query text, returning empty filter (matches all documents)");
                return Builders<BsonDocument>.Filter.Empty;
            }

            // The query text is not logged: its filter values are often emails, names or IDs
            BsonDocument bsonDocument;
            try
            {
                // Parse MongoDB JSON query format
                // Example input: {"status": "completed", "amount": {"$gt": 100}}
                // Example input: {"$and": [{"status": "active"}, {"age": {"$gte": 18}}]}
                bsonDocument = BsonDocument.Parse(queryText);
            }
            catch (MongoDB.Bson.BsonException ex)
            {
                _logger.LogError(ex, "BuildMongoFilter: Failed to parse MongoDB query");
                throw new ArgumentException($"Invalid MongoDB query format: {ex.Message}. Query must be valid MongoDB JSON (e.g., {{\"field\": \"value\"}}).", nameof(queryText), ex);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "BuildMongoFilter: Invalid JSON format in query text");
                throw new ArgumentException($"Invalid JSON format in query: {ex.Message}", nameof(queryText), ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BuildMongoFilter: Unexpected error parsing query text");
                throw new ArgumentException($"Error parsing MongoDB query: {ex.Message}", nameof(queryText), ex);
            }

            EnsureNoForbiddenOperators(bsonDocument);

            _logger.LogInformation("BuildMongoFilter: Successfully parsed MongoDB query with {ConditionCount} condition(s)", bsonDocument.ElementCount);

            return new BsonDocumentFilterDefinition<BsonDocument>(bsonDocument);
        }

        /// <summary>
        /// Refuses entity names that map onto platform collections rather than the tenant's own
        /// business data.
        /// </summary>
        /// <remarks>
        /// The tenant database is shared with IAM and with this service's own modules, and any
        /// signed-in user of the tenant can submit these queries. Without this check a query for
        /// <c>ClientCredential</c> returns every OAuth client secret, and one for <c>User</c> every
        /// user record, rendered into a file the caller can download. Prefixes cover whole
        /// families (<c>Subscription*</c>, <c>Payment*</c>, ...) so new internal collections are
        /// denied without an edit here. The identifier pattern also rules out <c>system.*</c>.
        /// </remarks>
        /// <exception cref="ArgumentException">The entity may not be queried.</exception>
        public static void EnsureEntityIsQueryable(string? entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName) || !EntityNamePattern.IsMatch(entityName))
            {
                throw new ArgumentException("EntityName must be a plain identifier.", nameof(entityName));
            }

            var collectionName = $"{entityName}s";
            if (DeniedCollections.Contains(collectionName) ||
                DeniedCollectionPrefixes.Any(prefix => collectionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Entity '{entityName}' is a platform collection and cannot be queried.", nameof(entityName));
            }
        }

        /// <summary>
        /// Refuses operators that run server-side code or evaluate expressions. <c>$where</c>,
        /// <c>$function</c> and <c>$accumulator</c> execute JavaScript on the database server, and
        /// <c>$expr</c> is the door <c>$function</c> needs in a find filter.
        /// </summary>
        private static void EnsureNoForbiddenOperators(BsonValue value)
        {
            switch (value)
            {
                case BsonDocument document:
                    foreach (var element in document)
                    {
                        if (ForbiddenOperators.Contains(element.Name))
                        {
                            throw new ArgumentException($"The '{element.Name}' operator is not allowed in template queries.");
                        }

                        EnsureNoForbiddenOperators(element.Value);
                    }
                    break;

                case BsonArray array:
                    foreach (var item in array)
                    {
                        EnsureNoForbiddenOperators(item);
                    }
                    break;
            }
        }

        /// <summary>
        /// Gets connection data with parent/child expansion
        /// TODO: Implement connection expansion logic
        /// </summary>
        public async Task<Dictionary<string, Dictionary<string, Dictionary<string, List<object>>>>> GetConnectionsWithEntityFromData(
            List<FilteredMongoQueryData> filteredQueryDatas, 
            string tenantId)
        {
            _logger.LogInformation("MongoQueryHelper: GetConnectionsWithEntityFromData START");
            var connectionsWithEntity = new Dictionary<string, Dictionary<string, Dictionary<string, List<object>>>>();

            foreach (var queryData in filteredQueryDatas)
            {
                if (!queryData.SolveConnectionForEntity)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("MongoQueryHelper: Processing connections for entity '{EntityName}'", queryData.EntityName);

                    // First get the entities
                    List<object> entities;
                    if (queryData.FetchAllMatchedItem)
                    {
                        entities = await GetAllMatchedDataWithoutPagination(queryData, tenantId);
                    }
                    else
                    {
                        entities = await GetDataByMongoQuery(queryData, tenantId);
                    }

                    var entityName = queryData.EntityName;

                    foreach (var objectItem in entities)
                    {
                        var jtoken = JToken.Parse(JsonConvert.SerializeObject(objectItem));
                        var entityItemId = jtoken["ItemId"]?.ToString();

                        if (string.IsNullOrEmpty(entityItemId))
                        {
                            continue;
                        }

                        // TODO: Implement actual connection expansion
                        // For now, log that this feature needs implementation
                        _logger.LogInformation("MongoQueryHelper: Connection expansion not fully implemented for EntityItemId={EntityItemId}", entityItemId);
                        
                        // Placeholder structure
                        if (!connectionsWithEntity.ContainsKey(entityName))
                        {
                            connectionsWithEntity[entityName] = new Dictionary<string, Dictionary<string, List<object>>>();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MongoQueryHelper: Error processing connections for entity '{EntityName}'", queryData.EntityName);
                }
            }

            _logger.LogInformation("MongoQueryHelper: GetConnectionsWithEntityFromData END");
            return connectionsWithEntity;
        }

        /// <summary>
        /// Converts metadata list to dictionary
        /// </summary>
        public static Dictionary<string, object> GetMetaDataListFromData(List<MetaData> metaDataList)
        {
            var metaDatas = new Dictionary<string, object>();

            foreach (var metadata in metaDataList)
            {
                if (!string.IsNullOrEmpty(metadata.Value))
                {
                    metaDatas.Add(metadata.Name, metadata.Value);
                }
                else if (metadata.Values != null)
                {
                    metaDatas.Add(metadata.Name, metadata.Values);
                }
            }

            return metaDatas;
        }
    }
}


