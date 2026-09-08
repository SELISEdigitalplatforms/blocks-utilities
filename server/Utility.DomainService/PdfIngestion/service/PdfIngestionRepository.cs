using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Utility.DomainService.PdfIngestion.Entities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfIngestion.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionRepository : IPdfIngestionRepository
    {
        private const string PdfIngestionJobs = "PdfIngestionJobs";

        private readonly ILogger<PdfIngestionRepository> _logger;
        private readonly IDbContextProvider _dbContextProvider;

        public PdfIngestionRepository(
            ILogger<PdfIngestionRepository> logger,
            IDbContextProvider dbContextProvider)
        {
            _logger = logger;
            _dbContextProvider = dbContextProvider;
        }

        /// <remarks>
        /// An upsert rather than an insert: the record is keyed by the file, and ingesting a file
        /// again is a new attempt on the same file, not a second thing to track.
        /// </remarks>
        public async Task<bool> SaveJobAsync(PdfIngestionJob job, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(job);

            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfIngestionJob>(PdfIngestionJobs);
                var filter = Builders<PdfIngestionJob>.Filter.Eq(j => j.Id, job.Id);

                await collection.ReplaceOneAsync(filter, job, new ReplaceOptions { IsUpsert = true });

                _logger.LogInformation("SaveJobAsync: Recorded ingestion of file {FileId}", LogSanitizer.Scrub(job.Id));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SaveJobAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }

        public async Task<PdfIngestionJob?> GetJobAsync(string fileId, string? tenantId = null)
        {
            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfIngestionJob>(PdfIngestionJobs);
                var filter = Builders<PdfIngestionJob>.Filter.Eq(j => j.Id, fileId);

                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetJobAsync for file {FileId}", LogSanitizer.Scrub(fileId));
                return null;
            }
        }

        /// <remarks>
        /// Reads several ingestion records in one round trip via an $in filter, rather than the batch
        /// status endpoint issuing one query per file.
        /// </remarks>
        public async Task<List<PdfIngestionJob>> GetJobsAsync(IEnumerable<string> fileIds, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(fileIds);
            var ids = fileIds.ToList();

            if (ids.Count == 0)
            {
                return [];
            }

            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfIngestionJob>(PdfIngestionJobs);
                var filter = Builders<PdfIngestionJob>.Filter.In(j => j.Id, ids);

                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetJobsAsync for {Count} file(s)", ids.Count);
                return [];
            }
        }

        /// <remarks>
        /// A failure here is logged and swallowed rather than thrown. The ingestion itself may have
        /// succeeded; losing the status write should leave the caller polling a stale record, not
        /// abandon a file that was already remediated.
        /// </remarks>
        public async Task<bool> UpdateJobAsync(PdfIngestionJob job, string? tenantId = null)
        {
            ArgumentNullException.ThrowIfNull(job);

            try
            {
                var tid = tenantId ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;
                var database = _dbContextProvider.GetDatabase(tid);
                var collection = database.GetCollection<PdfIngestionJob>(PdfIngestionJobs);
                var filter = Builders<PdfIngestionJob>.Filter.Eq(j => j.Id, job.Id);

                job.LastUpdateDate = DateTime.UtcNow;

                var result = await collection.ReplaceOneAsync(filter, job);

                if (result.MatchedCount == 0)
                {
                    _logger.LogWarning("UpdateJobAsync: No ingestion record for file {FileId}", LogSanitizer.Scrub(job.Id));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UpdateJobAsync for file {FileId}", LogSanitizer.Scrub(job.Id));
                return false;
            }
        }
    }
}
