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
    
        private const string DocumentConversionJobs = "DocumentConversionJobs";

        /// <summary>
        /// Records a newly accepted conversion.
        /// </summary>
        public async Task<bool> SaveDocumentConversionJobAsync(DocumentConversionJob job, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<DocumentConversionJob>(DocumentConversionJobs);

                await collection.InsertOneAsync(job);

                _logger.LogInformation("SaveDocumentConversionJobAsync: Recorded conversion {ConversionId}", job.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveDocumentConversionJobAsync for conversion {ConversionId}", job.Id);
                return false;
            }
        }

        /// <summary>
        /// Reads a conversion by its ID. Null when there is no such conversion, which the API turns
        /// into a 404 rather than inventing a state for a job nobody started.
        /// </summary>
        public async Task<DocumentConversionJob?> GetDocumentConversionJobAsync(string conversionId, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<DocumentConversionJob>(DocumentConversionJobs);
                var filter = Builders<DocumentConversionJob>.Filter.Eq(j => j.Id, conversionId);

                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDocumentConversionJobAsync for conversion {ConversionId}", conversionId);
                return null;
            }
        }

        /// <summary>
        /// Writes a conversion's new state.
        /// </summary>
        /// <remarks>
        /// A failure here is logged and swallowed rather than thrown. The conversion itself may have
        /// succeeded; losing the status write should leave the caller polling a stale record, not
        /// abandon a document that was already converted.
        /// </remarks>
        public async Task<bool> UpdateDocumentConversionJobAsync(DocumentConversionJob job, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<DocumentConversionJob>(DocumentConversionJobs);
                var filter = Builders<DocumentConversionJob>.Filter.Eq(j => j.Id, job.Id);

                job.LastUpdateDate = DateTime.UtcNow;

                var result = await collection.ReplaceOneAsync(filter, job);

                if (result.MatchedCount == 0)
                {
                    _logger.LogWarning("UpdateDocumentConversionJobAsync: No conversion {ConversionId} to update", job.Id);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateDocumentConversionJobAsync for conversion {ConversionId}", job.Id);
                return false;
            }
        }
}
}

