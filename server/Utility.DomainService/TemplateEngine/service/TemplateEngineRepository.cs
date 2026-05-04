using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Utility.DomainService.TemplateEngine.service
{
    /// <summary>
    /// Repository implementation for template engine data operations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class TemplateEngineRepository : ITemplateEngineRepository
    {
        private readonly ILogger<TemplateEngineRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;

        public TemplateEngineRepository(
            ILogger<TemplateEngineRepository> logger,
            IDbContextProvider dbContextProvider)
        {
            _logger = logger;
            _dbContextProvider = dbContextProvider;
        }

        public async Task<HtmlTemplate?> GetTemplateByIdAsync(string templateId)
        {
            try
            {
                _logger.LogInformation("GetTemplateByIdAsync for templateId: {TemplateId}", templateId);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<HtmlTemplate>("HtmlTemplates");
                var filter = Builders<HtmlTemplate>.Filter.Eq(t => t.ItemId, templateId);
                var template = await collection.Find(filter).FirstOrDefaultAsync();
                
                return template;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTemplateByIdAsync for templateId: {TemplateId}", templateId);
                return null;
            }
        }

        public async Task<bool> SaveTemplateAsync(HtmlTemplate template)
        {
            try
            {
                _logger.LogInformation("SaveTemplateAsync for template: {Name}", template.Name);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<HtmlTemplate>("HtmlTemplates");
                var filter = Builders<HtmlTemplate>.Filter.Eq(t => t.ItemId, template.ItemId);
                var options = new ReplaceOptions { IsUpsert = true };
                
                await collection.ReplaceOneAsync(filter, template, options);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveTemplateAsync for template: {Name}", template.Name);
                return false;
            }
        }

        public async Task<bool> TemplateExistsAsync(string templateId)
        {
            try
            {
                _logger.LogInformation("TemplateExistsAsync for templateId: {TemplateId}", templateId);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<HtmlTemplate>("HtmlTemplates");
                var filter = Builders<HtmlTemplate>.Filter.Eq(t => t.ItemId, templateId);
                var count = await collection.CountDocumentsAsync(filter);
                
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TemplateExistsAsync for templateId: {TemplateId}", templateId);
                return false;
            }
        }

        public async Task<List<PdfGenerationQuery>> GetPdfGenerationQueriesByDirectoryIdAsync(string directoryId)
        {
            try
            {
                _logger.LogInformation("GetPdfGenerationQueriesByDirectoryIdAsync for directoryId: {DirectoryId}", directoryId);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<PdfGenerationQuery>("PdfGenerationQueries");
                var filter = Builders<PdfGenerationQuery>.Filter.Eq(q => q.DirectoryId, directoryId);
                var queries = await collection.Find(filter).ToListAsync();
                
                return queries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPdfGenerationQueriesByDirectoryIdAsync for directoryId: {DirectoryId}", directoryId);
                return new List<PdfGenerationQuery>();
            }
        }

        public async Task<PdfGenerationQuery?> GetPdfGenerationQueryByIdAsync(string itemId)
        {
            try
            {
                _logger.LogInformation("GetPdfGenerationQueryByIdAsync for itemId: {ItemId}", itemId);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<PdfGenerationQuery>("PdfGenerationQueries");
                var filter = Builders<PdfGenerationQuery>.Filter.Eq(q => q.ItemId, itemId);
                var query = await collection.Find(filter).FirstOrDefaultAsync();
                
                return query;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPdfGenerationQueryByIdAsync for itemId: {ItemId}", itemId);
                return null;
            }
        }

        public async Task<string[]> GetUserReadableFieldsAsync(string entityName)
        {
            try
            {
                _logger.LogInformation("GetUserReadableFieldsAsync for entityName: {EntityName}", entityName);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<UserReadableData>("UserReadableDatas");
                var filter = Builders<UserReadableData>.Filter.Eq(u => u.EntityName, entityName);
                var userReadableData = await collection.Find(filter).FirstOrDefaultAsync();
                
                if (userReadableData == null)
                {
                    _logger.LogWarning("No UserReadableData found for entityName: {EntityName}", entityName);
                    return Array.Empty<string>();
                }
                
                return userReadableData.UserReadableFields ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetUserReadableFieldsAsync for entityName: {EntityName}", entityName);
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Get entity data by ItemId from MongoDB
        /// </summary>
        public async Task<string?> GetEntityByItemIdAsync(string entityName, string itemId)
        {
            try
            {
                _logger.LogInformation("GetEntityByItemIdAsync for entityName: {EntityName}, itemId: {ItemId}", entityName, itemId);
                
                var database = _dbContextProvider.GetDatabase(BlocksContext.GetContext()?.TenantId ?? "");
                var collection = database.GetCollection<BsonDocument>($"{entityName}s");
                var filter = Builders<BsonDocument>.Filter.Eq("_id", itemId);
                
                var bsonDocument = await collection.Find(filter).FirstOrDefaultAsync();
                if (bsonDocument == null)
                {
                    _logger.LogWarning("No entity found for entityName: {EntityName}, itemId: {ItemId}", entityName, itemId);
                    return null;
                }
                
                var result = bsonDocument.ToJson();
                if (string.IsNullOrEmpty(result)) return null;
                
                // Clean up ISODate formatting for JSON compatibility
                result = result.Replace("ISODate(", "");
                result = result.Replace("),", ",");
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetEntityByItemIdAsync for entityName: {EntityName}, itemId: {ItemId}", entityName, itemId);
                return null;
            }
        }
    }

    /// <summary>
    /// Represents user readable data configuration
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class UserReadableData
    {
        public string EntityName { get; set; } = string.Empty;
        public string[]? UserReadableFields { get; set; }
    }
}


