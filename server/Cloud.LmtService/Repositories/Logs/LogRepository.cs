using Blocks.Genesis;
using Cloud.LmtService.Models.Logs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Repositories.Logs
{
    public class LogRepository : ILogRepository
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoDatabase _archiveDatabase;
        private readonly ILogger<LogRepository> _logger;
        private const string FailedArchiveLogsCollection = "FailedArchiveLogs";

        public LogRepository(IBlocksSecret blocksSecret, IDbContextProvider dbContextProvider, ILogger<LogRepository> logger, IConfiguration configuration)
        {
            _database = dbContextProvider.GetDatabase(blocksSecret.LogConnectionString, blocksSecret.LogDatabaseName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        public async Task<IQueryable<LogProjection>> GetLogs(LiveLogRequest query)
        {
            var bc = BlocksContext.GetContext();
            var collection = _database.GetCollection<BsonDocument>(query.Name);
            var filter = Builders<BsonDocument>.Filter.Eq("TenantId", bc?.TenantId)
                & Builders<BsonDocument>.Filter.Gt("Timestamp", query.LastDate);
            var sort = Builders<BsonDocument>.Sort.Descending("Timestamp");
            var projection = Builders<BsonDocument>.Projection.As<LogProjection>();

            var logs = await collection
                .Find(filter)
                .Sort(sort)
                .Project(projection)
                .ToListAsync();

            return logs.AsQueryable();
        }

        public async Task<(IQueryable<LogProjection>, long)> GetLogs(GetLogsRequest query)
        {
            var bc = BlocksContext.GetContext();
            var collection = _database.GetCollection<BsonDocument>(query.ServiceName);
            var filter = Builders<BsonDocument>.Filter.Eq("TenantId", bc?.TenantId);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var regex = new BsonRegularExpression(query.Search, "i");
                filter &= Builders<BsonDocument>.Filter.Regex("Message", regex);
            }

            if (!string.IsNullOrWhiteSpace(query.Filter?.TraceId))
                filter &= Builders<BsonDocument>.Filter.Eq("TraceId", query.Filter.TraceId);

            if (!string.IsNullOrWhiteSpace(query.Filter?.SpanId))
                filter &= Builders<BsonDocument>.Filter.Eq("SpanId", query.Filter.SpanId);

            if (!string.IsNullOrWhiteSpace(query.Filter?.Level))
                filter &= Builders<BsonDocument>.Filter.Eq("Level", query.Filter.Level);

            if (query.Filter?.StartDate != null)
                filter &= Builders<BsonDocument>.Filter.Gt("Timestamp", query.Filter.StartDate);

            if (query.Filter?.EndDate != null)
                filter &= Builders<BsonDocument>.Filter.Lte("Timestamp", query.Filter.EndDate);

            var sort = Builders<BsonDocument>.Sort.Descending("Timestamp");
            var projection = Builders<BsonDocument>.Projection.As<LogProjection>();

            // Optimize: Execute count and data retrieval in parallel
            var countTask = collection.CountDocumentsAsync(filter);
            var logsTask = collection.Find(filter)
                .Sort(sort)
                .Project(projection)
                .Limit(query.PageSize)
                .Skip(query.PageSize * query.Page)
                .ToListAsync();

            await Task.WhenAll(countTask, logsTask);

            return (logsTask.Result.AsQueryable(), countTask.Result);
        }

        public async Task<(IQueryable<LogProjection>, long)> GetLogs(LogsByDateRequest request)
        {
            var bc = BlocksContext.GetContext();
            var collection = _database.GetCollection<BsonDocument>(request.ServiceName);
            var filter = Builders<BsonDocument>.Filter.Eq("TenantId", bc?.TenantId);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var regex = new BsonRegularExpression(request.Search, "i");
                filter &= Builders<BsonDocument>.Filter.Regex("Message", regex);
            }

            if (!string.IsNullOrWhiteSpace(request.Filter?.TraceId))
                filter &= Builders<BsonDocument>.Filter.Eq("TraceId", request.Filter.TraceId);

            if (!string.IsNullOrWhiteSpace(request.Filter?.SpanId))
                filter &= Builders<BsonDocument>.Filter.Eq("SpanId", request.Filter.SpanId);

            if (!string.IsNullOrWhiteSpace(request.Filter?.Level))
                filter &= Builders<BsonDocument>.Filter.Eq("Level", request.Filter.Level);

            var sort = Builders<BsonDocument>.Sort.Descending("Timestamp");

            if (request.Filter?.StartDate != null)
                filter &= Builders<BsonDocument>.Filter.Gte("Timestamp", request.Filter.StartDate);

            if (request.Filter?.EndDate != null)
                filter &= Builders<BsonDocument>.Filter.Lt("Timestamp", request.Filter.EndDate);

            var projection = Builders<BsonDocument>.Projection.As<LogProjection>();

            // Optimize: Execute count and data retrieval in parallel
            var countTask = collection.CountDocumentsAsync(filter);
            var logsTask = collection.Find(filter)
                .Sort(sort)
                .Project(projection)
                .Limit(request.PageSize)
                .ToListAsync();

            await Task.WhenAll(countTask, logsTask);

            return (logsTask.Result.AsQueryable(), countTask.Result);
        }
    }
}
