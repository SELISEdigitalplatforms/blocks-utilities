using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfIngestion.Entities;
using Utility.DomainService.PdfIngestion.Events;
using Utility.DomainService.PdfIngestion.Utilities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfIngestion.service
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfIngestionService : IPdfIngestionService
    {
        /// <summary>
        /// The most files one request may name. See <c>DocumentConversionService.MaxBatchSize</c>
        /// for why this is bounded rather than left open.
        /// </summary>
        internal const int MaxBatchSize = 50;

        private readonly ILogger<PdfIngestionService> _logger;
        private readonly IMessageClient _messageClient;
        private readonly IPdfIngestionRepository _repository;
        private readonly PdfStorageHelper _storageHelper;

        public PdfIngestionService(
            ILogger<PdfIngestionService> logger,
            IMessageClient messageClient,
            IPdfIngestionRepository repository,
            PdfStorageHelper storageHelper)
        {
            _logger = logger;
            _messageClient = messageClient;
            _repository = repository;
            _storageHelper = storageHelper;
        }

        /// <inheritdoc />
        public async Task<PdfIngestionResult<IngestPdfsBatchResponse>> RequestIngestionsAsync(
            IngestPdfsRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // A duplicate ID is one request for that file, not two, so queuing it twice would just
            // race two workers over the same replacement.
            var fileIds = (request.FileIds ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fileIds.Count == 0)
            {
                return PdfIngestionResult<IngestPdfsBatchResponse>.Failure(
                    PdfIngestionFailureKind.Validation,
                    "file_ids_required",
                    "fileIds must contain at least one file ID.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = ["fileIds must contain at least one file ID."]
                    });
            }

            if (fileIds.Count > MaxBatchSize)
            {
                return PdfIngestionResult<IngestPdfsBatchResponse>.Failure(
                    PdfIngestionFailureKind.Validation,
                    "too_many_files",
                    $"A single request can ingest at most {MaxBatchSize} files; {fileIds.Count} were sent.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = [$"A single request can ingest at most {MaxBatchSize} files."]
                    });
            }

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var userId = BlocksContext.GetContext()?.UserId;
            var results = new List<PdfIngestionAcceptance>(fileIds.Count);

            // Sequential rather than fanned out with Task.WhenAll: each file writes to the same
            // tenant database and publishes to the same queue, so nothing here benefits from
            // concurrency the way the read side's storage lookups do, and staying sequential keeps
            // the per-file logging in request order.
            foreach (var fileId in fileIds)
            {
                results.Add(await RequestOneIngestion(fileId, request.MessageCoRelationId, request.DryRun, tenantId, userId));
            }

            var accepted = results.Count(r => r.Accepted);

            return PdfIngestionResult<IngestPdfsBatchResponse>.Success(
                new IngestPdfsBatchResponse
                {
                    MessageCoRelationId = request.MessageCoRelationId,
                    Results = results,
                    AcceptedCount = accepted,
                    RejectedCount = results.Count - accepted
                },
                correlationId);
        }

        /// <summary>
        /// Records and queues the ingestion of one file. Never throws - any failure becomes a
        /// rejected <see cref="PdfIngestionAcceptance"/>, so one bad file cannot abort the rest of
        /// the batch the caller is waiting on.
        /// </summary>
        private async Task<PdfIngestionAcceptance> RequestOneIngestion(
            string fileId,
            string? messageCoRelationId,
            bool dryRun,
            string tenantId,
            string? userId)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return new PdfIngestionAcceptance
                {
                    FileId = fileId ?? string.Empty,
                    Accepted = false,
                    ErrorCode = "input_file_id_required",
                    ErrorMessage = "A file ID in the list was blank."
                };
            }

            var now = DateTime.UtcNow;

            var job = new PdfIngestionJob
            {
                Id = fileId,
                MessageCoRelationId = messageCoRelationId,
                Status = PdfIngestionStatus.Queued,
                TenantId = tenantId,
                UserId = userId,
                CreatedBy = userId,
                DryRun = dryRun,
                CreateDate = now,
                LastUpdateDate = now
            };

            // The record is written before the event is published, deliberately - see
            // DocumentConversionService.RequestOneConversion for why.
            if (!await _repository.SaveJobAsync(job))
            {
                return new PdfIngestionAcceptance
                {
                    FileId = fileId,
                    Accepted = false,
                    ErrorCode = "ingestion_not_recorded",
                    ErrorMessage = "The ingestion could not be recorded and was not started."
                };
            }

            try
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<IngestPdfEvent>
                    {
                        ConsumerName = PdfIngestionConstants.IngestPdfQueue,
                        Payload = new IngestPdfEvent
                        {
                            FileId = fileId,
                            MessageCoRelationId = messageCoRelationId,
                            ProjectKey = tenantId,
                            UserId = userId,
                            DryRun = dryRun
                        }
                    });
            }
            catch (Exception ex)
            {
                // The record exists but nothing will ever pick it up, so it is failed here rather
                // than left Queued forever - a poller deserves an answer, not a permanent maybe.
                _logger.LogError(
                    ex,
                    "RequestIngestionsAsync: Failed to queue ingestion of file {FileId}",
                    LogSanitizer.Scrub(fileId));

                job.Status = PdfIngestionStatus.Failed;
                job.ErrorCode = "ingestion_not_queued";
                job.ErrorMessage = "The ingestion could not be queued.";
                job.CompletedDate = DateTime.UtcNow;
                await _repository.UpdateJobAsync(job);

                return new PdfIngestionAcceptance
                {
                    FileId = fileId,
                    Accepted = false,
                    ErrorCode = "ingestion_not_queued",
                    ErrorMessage = "The ingestion could not be queued."
                };
            }

            _logger.LogInformation(
                "RequestIngestionsAsync: Queued ingestion of file {FileId}",
                LogSanitizer.Scrub(fileId));

            return new PdfIngestionAcceptance
            {
                FileId = fileId,
                Accepted = true,
                Status = job.Status
            };
        }

        /// <inheritdoc />
        public async Task<PdfIngestionResult<PdfIngestionStatusBatchResponse>> GetStatusAsync(
            GetPdfIngestionStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var fileIds = (request.FileIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fileIds.Count == 0)
            {
                return PdfIngestionResult<PdfIngestionStatusBatchResponse>.Failure(
                    PdfIngestionFailureKind.Validation,
                    "file_ids_required",
                    "fileIds must contain at least one file ID.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = ["fileIds must contain at least one file ID."]
                    });
            }

            if (fileIds.Count > MaxBatchSize)
            {
                return PdfIngestionResult<PdfIngestionStatusBatchResponse>.Failure(
                    PdfIngestionFailureKind.Validation,
                    "too_many_files",
                    $"A single request can query at most {MaxBatchSize} files; {fileIds.Count} were sent.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = [$"A single request can query at most {MaxBatchSize} files."]
                    });
            }

            var jobs = await _repository.GetJobsAsync(fileIds);
            var jobsById = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);

            // Storage is consulted once per completed file, and those lookups are independent of
            // one another, so they run concurrently rather than serially the way the write side does.
            var resolutions = await Task.WhenAll(fileIds.Select(fileId => BuildStatus(fileId, jobsById)));

            return PdfIngestionResult<PdfIngestionStatusBatchResponse>.Success(
                new PdfIngestionStatusBatchResponse { Results = [.. resolutions] },
                correlationId);
        }

        private async Task<PdfIngestionStatusResult> BuildStatus(
            string fileId,
            IReadOnlyDictionary<string, PdfIngestionJob> jobsById)
        {
            if (!jobsById.TryGetValue(fileId, out var job))
            {
                return new PdfIngestionStatusResult
                {
                    FileId = fileId,
                    Found = false,
                    ErrorCode = "ingestion_not_found",
                    ErrorMessage = "That file has not been submitted for ingestion."
                };
            }

            var isComplete = job.Status is PdfIngestionStatus.Completed or PdfIngestionStatus.Failed;

            var result = new PdfIngestionStatusResult
            {
                FileId = job.Id,
                Found = true,
                FileName = job.FileName,
                MessageCoRelationId = job.MessageCoRelationId,
                Status = job.Status,
                IsComplete = isComplete,
                ErrorCode = job.ErrorCode,
                ErrorMessage = job.ErrorMessage,
                RequestedAtUtc = job.CreateDate,
                CompletedAtUtc = job.CompletedDate
            };

            var verdict = job.Verdict;
            if (verdict != null)
            {
                result.PageCount = verdict.PageCount;
                result.WasReadable = verdict.WasReadable;
                result.IsReadable = verdict.IsReadable;
                result.RepairedWithQpdf = verdict.RepairedWithQpdf;
                result.GeometryWasReadable = verdict.GeometryWasReadable;
                result.GeometryIsReadable = verdict.GeometryIsReadable;
                result.GeometryNormalized = verdict.GeometryNormalized;
                result.PageGeometry = verdict.PageGeometry;
                result.GeometrySkippedReason = verdict.GeometrySkippedReason;
                result.HasSignature = verdict.HasSignature;
                result.SignatureCount = verdict.SignatureCount;
                result.MetadataClaim = verdict.MetadataClaim;
                result.ClaimedProfile = verdict.ClaimedProfile;
                result.ValidatedProfile = verdict.ValidatedProfile;
                result.IsPdfA = verdict.IsPdfA;
                result.IsCompliant = verdict.IsCompliant;
                result.PdfAFailedChecks = verdict.PdfAFailedChecks;
                result.PdfARepairAttempted = verdict.PdfARepairAttempted;
                result.PdfARepairSucceeded = verdict.PdfARepairSucceeded;
                result.FinalProfile = verdict.FinalProfile;
                result.ContentChanged = verdict.ContentChanged;
                result.Stages = verdict.Stages;
            }

            if (job.Status == PdfIngestionStatus.Completed)
            {
                // Resolved per request rather than stored: a storage URL is time-limited, so one
                // written at completion time would be expired by the time a poller asked for it.
                var record = await _storageHelper.GetFileRecord(job.Id);

                if (record == null)
                {
                    _logger.LogWarning(
                        "GetStatusAsync: Ingestion of file {FileId} completed but the file could not be resolved",
                        LogSanitizer.Scrub(job.Id));
                }
                else
                {
                    result.DownloadUrl = record.Url;
                    result.FileName ??= record.Name;
                }
            }

            return result;
        }
    }
}
