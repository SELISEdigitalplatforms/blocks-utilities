using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfSignatureValidation.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public sealed class PdfSignatureValidationRepository : IPdfSignatureValidationRepository
    {
        private const string PdfSignatureValidationJobs = "PdfSignatureValidationJobs";

        private readonly ILogger<PdfSignatureValidationRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;

        public PdfSignatureValidationRepository(
            ILogger<PdfSignatureValidationRepository> logger,
            IDbContextProvider dbContextProvider)
        {
            _logger = logger;
            _dbContextProvider = dbContextProvider;
        }

        /// <remarks>
        /// An upsert, as for ingestion: validating a file again replaces its record. The new record
        /// carries a new run ID, which is what turns any run still in flight into a superseded one.
        /// </remarks>
        public async Task<bool> SaveJobAsync(PdfSignatureValidationJob job, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(job);

            try
            {
                var filter = Builders<PdfSignatureValidationJob>.Filter.Eq(j => j.Id, job.Id);

                await Collection(tenantId).ReplaceOneAsync(filter, job, new ReplaceOptions { IsUpsert = true });

                _logger.LogInformation("SaveJobAsync: Recorded signature validation of file {FileId}", LogSanitizer.Scrub(job.Id));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveJobAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }

        public async Task<List<PdfSignatureValidationJob>> GetJobsAsync(IEnumerable<string> fileIds, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            var ids = fileIds.ToList();

            if (ids.Count == 0)
            {
                return [];
            }

            try
            {
                var filter = Builders<PdfSignatureValidationJob>.Filter.In(j => j.Id, ids);

                return await Collection(tenantId).Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetJobsAsync for {Count} file(s)", ids.Count);
                return [];
            }
        }

        /// <remarks>
        /// The run ID is part of the filter, so the check and the write are one atomic operation: a
        /// re-request that lands between them cannot be overwritten. A miss is logged at information,
        /// not warning, because a superseded run is an expected outcome of re-requesting a file.
        /// </remarks>
        public async Task<bool> UpdateJobIfCurrentRunAsync(PdfSignatureValidationJob job, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(job);

            try
            {
                var filter = Builders<PdfSignatureValidationJob>.Filter.And(
                    Builders<PdfSignatureValidationJob>.Filter.Eq(j => j.Id, job.Id),
                    Builders<PdfSignatureValidationJob>.Filter.Eq(j => j.RunId, job.RunId));

                var result = await Collection(tenantId).ReplaceOneAsync(filter, job);

                if (result.MatchedCount == 0)
                {
                    _logger.LogInformation(
                        "UpdateJobIfCurrentRunAsync: Run {RunId} of file {FileId} was superseded; its write was discarded",
                        LogSanitizer.Scrub(job.RunId),
                        LogSanitizer.Scrub(job.Id));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateJobIfCurrentRunAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }

        private IMongoCollection<PdfSignatureValidationJob> Collection(string? tenantId)
        {
            var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
            return _dbContextProvider.GetDatabase(tid).GetCollection<PdfSignatureValidationJob>(PdfSignatureValidationJobs);
        }
    }
}
