using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Utility.DomainService.PdfGenerator.Entities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Repository implementation for PDF generator data operations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfGeneratorRepository : IPdfGeneratorRepository
    {
        private readonly ILogger<PdfGeneratorRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;

        public PdfGeneratorRepository(
            ILogger<PdfGeneratorRepository> logger,
            IDbContextProvider dbContextProvider)
        {
            _logger = logger;
            _dbContextProvider = dbContextProvider;
        }

        /// <summary>
        /// Get PDF utility profile by ID
        /// </summary>
        public async Task<PdfUtilityProfile?> GetPdfUtilityProfileAsync(string profileId, string? tenantId = null)
        {
            try
            {
                _logger.LogInformation("GetPdfUtilityProfileAsync for profileId: {ProfileId}", profileId);
                
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfUtilityProfile>("PdfUtilityProfiles");
                var filter = Builders<PdfUtilityProfile>.Filter.Eq(p => p.Id, profileId);
                var profile = await collection.Find(filter).FirstOrDefaultAsync();
                
                if (profile == null)
                {
                    _logger.LogWarning("PDF Utility Profile not found for profileId: {ProfileId}", profileId);
                }
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPdfUtilityProfileAsync for profileId: {ProfileId}", profileId);
                return null;
            }
        }

        /// <summary>
        /// Save PDF extract dump to database
        /// </summary>
        public async Task<bool> SavePdfExtractDumpAsync(PdfExtractDump extractDump, string? tenantId = null)
        {
            try
            {
                _logger.LogInformation("SavePdfExtractDumpAsync for RecordId: {RecordId}", extractDump.ItemId);
                
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfExtractDump>("PdfExtractDumps");
                
                await collection.InsertOneAsync(extractDump);
                
                _logger.LogInformation("PDF Extract Dump saved successfully for RecordId: {RecordId}", extractDump.ItemId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SavePdfExtractDumpAsync for RecordId: {RecordId}", extractDump.ItemId);
                return false;
            }
        }

        /// <summary>
        /// Check if PDF extract dump exists by RecordId
        /// </summary>
        public async Task<bool> PdfExtractDumpExistsAsync(string recordId, string? tenantId = null)
        {
            try
            {
                _logger.LogInformation("PdfExtractDumpExistsAsync for recordId: {RecordId}", recordId);
                
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfExtractDump>("PdfExtractDumps");
                var filter = Builders<PdfExtractDump>.Filter.Eq(p => p.ItemId, recordId);
                var count = await collection.CountDocumentsAsync(filter);
                
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PdfExtractDumpExistsAsync for recordId: {RecordId}", recordId);
                return false;
            }
        }

        /// <summary>
        /// Get PDF extract dump by RecordId
        /// </summary>
        public async Task<PdfExtractDump?> GetPdfExtractDumpAsync(string recordId, string? tenantId = null)
        {
            try
            {
                _logger.LogInformation("GetPdfExtractDumpAsync for recordId: {RecordId}", recordId);
                
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfExtractDump>("PdfExtractDumps");
                var filter = Builders<PdfExtractDump>.Filter.Eq(p => p.ItemId, recordId);
                var extractDump = await collection.Find(filter).FirstOrDefaultAsync();
                
                return extractDump;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPdfExtractDumpAsync for recordId: {RecordId}", recordId);
                return null;
            }
        }
    }
}

