using Blocks.Genesis;
using Cloud.LmtService.Models.Trace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Repositories.Trace
{
    public class TraceRepository:ITraceRepository
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoDatabase _archiveDatabase;
        private readonly ILogger<TraceRepository> _logger;
        public TraceRepository(
            IBlocksSecret blocksSecret,
            IDbContextProvider dbContextProvider,
            ILogger<TraceRepository> logger,
            IConfiguration configuration)
        {
            _database = dbContextProvider.GetDatabase(blocksSecret.TraceConnectionString, blocksSecret.TraceDatabaseName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public async Task<IQueryable<SingleTraceProjection>> GetTraces(GetTraceRequest query)
        {
            var bc = BlocksContext.GetContext();
            var collection = _database.GetCollection<BsonDocument>(bc?.TenantId);

            var filter = Builders<BsonDocument>.Filter.Eq("TraceId", query.TraceId);
            var projection = Builders<BsonDocument>.Projection.As<SingleTraceProjection>();

            var logs = await collection
                .Find(filter)
                .Project(projection)
                .ToListAsync();

            return logs.AsQueryable();
        }

        public async Task<(IQueryable<TraceProjection>, long)> GetTraces(GetTracesRequest query)
        {
            var bc = BlocksContext.GetContext();
            var collection = _database.GetCollection<BsonDocument>(bc.TenantId);

            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("ParentId", string.Empty),
                Builders<BsonDocument>.Filter.Eq("ParentId", BsonNull.Value)
            );

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var regex = new BsonRegularExpression(query.Search, "i");

                filter &= Builders<BsonDocument>.Filter.Or(
                          Builders<BsonDocument>.Filter.Regex("OperationName", regex),
                          Builders<BsonDocument>.Filter.Eq("TraceId", query.Search));
            }

            if (query.Filter?.Services != null && query.Filter.Services.Count > 0)
                filter &= Builders<BsonDocument>.Filter.In("ServiceName", query.Filter.Services);

            if (query.Filter?.Excepts != null && query.Filter.Excepts.Count > 0)
                filter &= Builders<BsonDocument>.Filter.Nin("ServiceName", query.Filter.Excepts);

            if (query.Filter?.StartDate != null)
                filter &= Builders<BsonDocument>.Filter.Gt("Timestamp", query.Filter.StartDate);

            if (query.Filter?.EndDate != null)
                filter &= Builders<BsonDocument>.Filter.Lte("Timestamp", query.Filter.EndDate);

            if (query.Filter?.StatusCodes != null && query.Filter.StatusCodes.Count > 0)
            {
                var statusCodeField = new BsonDocument("$getField", new BsonDocument
                {
                    { "field", "response.status.code" },
                    { "input", "$Attributes" }
                });

                var inArray = new BsonArray(query.Filter.StatusCodes);
                var exprFilter = new BsonDocument("$expr", new BsonDocument("$in", new BsonArray { statusCodeField, inArray }));

                filter &= new BsonDocumentFilterDefinition<BsonDocument>(exprFilter);
            }

            var sort = query.Sort != null
                ? (query.Sort.IsDescending
                    ? Builders<BsonDocument>.Sort.Descending(query.Sort.Property)
                    : Builders<BsonDocument>.Sort.Ascending(query.Sort.Property))
                : Builders<BsonDocument>.Sort.Descending("Timestamp");

            var projection = Builders<BsonDocument>.Projection.As<TraceProjection>();

            // Optimize: Run count and data fetch in parallel
            var tracesTask = collection.Find(filter)
                                       .Sort(sort)
                                       .Project(projection)
                                       .Limit(query.PageSize)
                                       .Skip(query.PageSize * query.Page)
                                       .ToListAsync();

            var countTask = collection.CountDocumentsAsync(filter);

            await Task.WhenAll(tracesTask, countTask);

            return (tracesTask.Result.AsQueryable(), countTask.Result);
        }

        public async Task<object> GetOperationalAnalytics(
            DateTime startTime,
            DateTime endTime,
            string serviceName,
            string? operationSearch = null)
        {
            var filter = Builders<BsonDocument>.Filter.Gte("Timestamp", startTime) &
                         Builders<BsonDocument>.Filter.Lte("Timestamp", endTime) &
                         Builders<BsonDocument>.Filter.Eq("ServiceName", serviceName);

            if (!string.IsNullOrWhiteSpace(operationSearch))
            {
                var regex = new BsonRegularExpression(operationSearch, "i");
                filter &= Builders<BsonDocument>.Filter.Regex("OperationName", regex);
            }

            return await RunAnalyticsQuery(filter, "OperationName");
        }

        public async Task<object> GetServiceAnalytics(
            DateTime startTime,
            DateTime endTime,
            string? serviceName = null)
        {
            var filter = Builders<BsonDocument>.Filter.Gte("Timestamp", startTime) &
                         Builders<BsonDocument>.Filter.Lte("Timestamp", endTime) &
                         Builders<BsonDocument>.Filter.Eq("Attributes.usage", true);

            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                filter &= Builders<BsonDocument>.Filter.Eq("ServiceName", serviceName);
            }

            return await RunAnalyticsQuery(filter, "ServiceName");
        }
        private async Task<List<Dictionary<string, object>>> RunAnalyticsQuery(FilterDefinition<BsonDocument> filter, string groupBy)
        {
            var collection = GetTenantCollection();

            var statusCodeField = new BsonDocument("$toInt",
                new BsonDocument("$getField", new BsonDocument
                {
                    { "field", "response.status.code" },
                    { "input", "$Attributes" }
                })
            );

            var throughputField = new BsonDocument("$toInt", new BsonDocument("$getField", new BsonDocument
                {
                    { "field", "throughput.total.bytes" },
                    { "input", "$Attributes" }
                })
            );

            var groupStage = new BsonDocument
            {
                { "_id", $"${groupBy}" },
                { "TotalRequests", new BsonDocument("$sum", 1) },
                { "Status1xx", CreateStatusRangeSum(statusCodeField, 100, 200) },
                { "Status2xx", CreateStatusRangeSum(statusCodeField, 200, 300) },
                { "Status3xx", CreateStatusRangeSum(statusCodeField, 300, 400) },
                { "Status4xx", CreateStatusRangeSum(statusCodeField, 400, 500) },
                { "Status5xx", CreateStatusRangeSum(statusCodeField, 500, null) },

                { "TotalDuration", new BsonDocument("$sum", "$Duration") },
                { "AverageDuration", new BsonDocument("$avg", "$Duration") },
                { "PeakDuration", new BsonDocument("$max", "$Duration") },
                { "TotalThroughput", new BsonDocument("$sum", throughputField) },
                { "AverageThroughput", new BsonDocument("$avg", throughputField) }
            };

            var rawResult = await collection.Aggregate()
                .Match(filter)
                .Group(groupStage)
                .ToListAsync();

            return [.. rawResult.Select(doc => doc.ToDictionary())];
        }

        private static BsonDocument CreateStatusRangeSum(BsonDocument statusCodeField, int min, int? maxExclusive)
        {
            BsonValue condition;
            if (maxExclusive.HasValue)
            {
                condition = new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument("$gte", new BsonArray { statusCodeField, min }),
                    new BsonDocument("$lt", new BsonArray { statusCodeField, maxExclusive.Value })
                });
            }
            else
            {
                condition = new BsonDocument("$gte", new BsonArray { statusCodeField, min });
            }

            return new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
            {
                condition,
                1,
                0
            }));
        }


        private IMongoCollection<BsonDocument> GetTenantCollection()
        {
            var bc = BlocksContext.GetContext();
            return _database.GetCollection<BsonDocument>(bc.TenantId);
        }

    }
}
