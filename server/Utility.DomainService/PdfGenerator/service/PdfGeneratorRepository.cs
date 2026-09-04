using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.Shared.Utilities;

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
        /// Records a conversion, replacing any earlier one for the same file.
        /// </summary>
        /// <remarks>
        /// An upsert rather than an insert: the record is keyed by the file, and converting a file
        /// again is a new attempt on the same file, not a second thing to track. An insert would
        /// fail on the duplicate key and reject a perfectly reasonable retry.
        /// </remarks>
        public async Task<bool> SaveDocumentConversionJobAsync(DocumentConversionJob job, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<DocumentConversionJob>(DocumentConversionJobs);
                var filter = Builders<DocumentConversionJob>.Filter.Eq(j => j.Id, job.Id);

                await collection.ReplaceOneAsync(filter, job, new ReplaceOptions { IsUpsert = true });

                _logger.LogInformation("SaveDocumentConversionJobAsync: Recorded conversion of file {FileId}", LogSanitizer.Scrub(job.Id));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveDocumentConversionJobAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }

        /// <summary>
        /// Reads the conversion state of a file. Null when that file has never been submitted for
        /// conversion, which the API turns into a 404 rather than inventing a state for it.
        /// </summary>
        public async Task<DocumentConversionJob?> GetDocumentConversionJobAsync(string fileId, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? "";
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<DocumentConversionJob>(DocumentConversionJobs);
                var filter = Builders<DocumentConversionJob>.Filter.Eq(j => j.Id, fileId);

                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDocumentConversionJobAsync for file {FileId}", LogSanitizer.Scrub(fileId));
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
                    _logger.LogWarning("UpdateDocumentConversionJobAsync: No conversion record for file {FileId}", LogSanitizer.Scrub(job.Id));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateDocumentConversionJobAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }
}
}

