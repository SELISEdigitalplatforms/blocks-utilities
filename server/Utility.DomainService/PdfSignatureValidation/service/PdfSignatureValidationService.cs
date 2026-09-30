using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.PdfSignatureValidation.Events;
using Utility.DomainService.PdfSignatureValidation.Utilities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfSignatureValidation.service
{
    /// <inheritdoc />
    public sealed class PdfSignatureValidationService : IPdfSignatureValidationService
    {
        /// <summary>The most entries one request may carry, as for ingestion.</summary>
        internal const int MaxBatchSize = 50;

        private readonly ILogger<PdfSignatureValidationService> _logger;
        private readonly IMessageClient _messageClient;
        private readonly IPdfSignatureValidationRepository _repository;
        private readonly TimeProvider _timeProvider;

        public PdfSignatureValidationService(
            ILogger<PdfSignatureValidationService> logger,
            IMessageClient messageClient,
            IPdfSignatureValidationRepository repository,
            TimeProvider timeProvider)
        {
            _logger = logger;
            _messageClient = messageClient;
            _repository = repository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<PdfIngestionResult<ValidatePdfSignaturesBatchResponse>> RequestValidationsAsync(
            ValidatePdfSignaturesRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Counted as sent, before anything is removed: unlike ingestion, every entry gets its own
            // result, so the limit is on how many results one response carries.
            var entries = request.FileIds ?? [];

            if (entries.Count == 0)
            {
                return ValidationFailure<ValidatePdfSignaturesBatchResponse>(
                    "file_ids_required", "fileIds must contain at least one file ID.", correlationId);
            }

            if (entries.Count > MaxBatchSize)
            {
                return ValidationFailure<ValidatePdfSignaturesBatchResponse>(
                    "too_many_files",
                    $"A single request can validate at most {MaxBatchSize} files; {entries.Count} were sent.",
                    correlationId);
            }

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var userId = BlocksContext.GetContext()?.UserId;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<PdfSignatureValidationAcceptance>(entries.Count);

            // Sequential, as for ingestion: every entry writes to the same tenant database and the
            // same queue, and staying sequential keeps results and logs in request order.
            foreach (var fileId in entries)
            {
                if (string.IsNullOrWhiteSpace(fileId))
                {
                    results.Add(Rejected(fileId ?? string.Empty, "input_file_id_required", "A file ID in the list was blank."));
                }
                else if (!seen.Add(fileId))
                {
                    // The first occurrence was queued; queuing it again would only race two runs of
                    // the same file, and the second would supersede the first for nothing.
                    results.Add(Rejected(fileId, "input_file_id_duplicate", "This file ID appears earlier in the list."));
                }
                else
                {
                    results.Add(await RequestOneValidation(fileId, request.MessageCoRelationId, tenantId, userId));
                }
            }

            var accepted = results.Count(r => r.Accepted);

            return PdfIngestionResult<ValidatePdfSignaturesBatchResponse>.Success(
                new ValidatePdfSignaturesBatchResponse
                {
                    MessageCoRelationId = request.MessageCoRelationId,
                    Results = results,
                    AcceptedCount = accepted,
                    RejectedCount = results.Count - accepted
                },
                correlationId);
        }

        /// <summary>
        /// Records and queues the validation of one file under a new run ID. Never throws: any
        /// failure becomes a rejected result, so one bad file cannot abort the rest of the batch.
        /// </summary>
        private async Task<PdfSignatureValidationAcceptance> RequestOneValidation(
            string fileId,
            string? messageCoRelationId,
            string tenantId,
            string? userId)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var job = new PdfSignatureValidationJob
            {
                Id = fileId,
                RunId = Guid.NewGuid().ToString("N"),
                MessageCoRelationId = messageCoRelationId,
                Status = PdfIngestionStatus.Queued,
                TenantId = tenantId,
                UserId = userId,
                CreatedBy = userId,
                CreateDate = now,
                LastUpdateDate = now
            };

            // Recorded before it is published, as for ingestion: a worker that picks the event up
            // must find the record, and its run ID, already there.
            if (!await _repository.SaveJobAsync(job))
            {
                return Rejected(fileId, "validation_not_recorded", "The validation could not be recorded and was not started.");
            }

            try
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<ValidatePdfSignaturesEvent>
                    {
                        ConsumerName = PdfSignatureValidationConstants.ValidatePdfSignaturesQueue,
                        Payload = new ValidatePdfSignaturesEvent
                        {
                            FileId = fileId,
                            RunId = job.RunId,
                            MessageCoRelationId = messageCoRelationId,
                            ProjectKey = tenantId,
                            UserId = userId
                        }
                    });
            }
            catch (Exception ex)
            {
                // Nothing will ever pick this run up, so it is failed here rather than left Queued
                // forever. Conditional on the run ID like every other write: if the file was
                // re-requested meanwhile, the newer record is not this failure's to overwrite.
                _logger.LogError(
                    ex,
                    "RequestValidationsAsync: Failed to queue signature validation of file {FileId}",
                    LogSanitizer.Scrub(fileId));

                var failedAt = _timeProvider.GetUtcNow().UtcDateTime;
                job.Status = PdfIngestionStatus.Failed;
                job.ErrorCode = "validation_not_queued";
                job.ErrorMessage = "The validation could not be queued.";
                job.CompletedDate = failedAt;
                job.LastUpdateDate = failedAt;
                await _repository.UpdateJobIfCurrentRunAsync(job);

                return Rejected(fileId, "validation_not_queued", "The validation could not be queued.");
            }

            _logger.LogInformation(
                "RequestValidationsAsync: Queued signature validation of file {FileId}",
                LogSanitizer.Scrub(fileId));

            return new PdfSignatureValidationAcceptance
            {
                FileId = fileId,
                Accepted = true,
                Status = job.Status
            };
        }

        /// <inheritdoc />
        public async Task<PdfIngestionResult<PdfSignatureValidationStatusBatchResponse>> GetStatusAsync(
            GetPdfSignatureValidationStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // As ingestion's status endpoint: reading has no side effects, so a blank or repeated ID
            // is simply not asked about twice, and the caller gets one result per distinct ID.
            var fileIds = (request.FileIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fileIds.Count == 0)
            {
                return ValidationFailure<PdfSignatureValidationStatusBatchResponse>(
                    "file_ids_required", "fileIds must contain at least one file ID.", correlationId);
            }

            if (fileIds.Count > MaxBatchSize)
            {
                return ValidationFailure<PdfSignatureValidationStatusBatchResponse>(
                    "too_many_files",
                    $"A single request can query at most {MaxBatchSize} files; {fileIds.Count} were sent.",
                    correlationId);
            }

            // The repository reads the caller's tenant database, so another tenant's job is simply
            // not there and answers found: false like a file never submitted.
            var jobs = await _repository.GetJobsAsync(fileIds);
            var jobsById = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);

            return PdfIngestionResult<PdfSignatureValidationStatusBatchResponse>.Success(
                new PdfSignatureValidationStatusBatchResponse
                {
                    Results = [.. fileIds.Select(fileId => BuildStatus(fileId, jobsById))]
                },
                correlationId);
        }

        private static PdfSignatureValidationStatusResult BuildStatus(
            string fileId,
            Dictionary<string, PdfSignatureValidationJob> jobsById)
        {
            if (!jobsById.TryGetValue(fileId, out var job))
            {
                return new PdfSignatureValidationStatusResult
                {
                    FileId = fileId,
                    Found = false,
                    ErrorCode = "validation_not_found",
                    ErrorMessage = "That file has not been submitted for signature validation."
                };
            }

            return new PdfSignatureValidationStatusResult
            {
                FileId = job.Id,
                Found = true,
                Status = job.Status,
                IsComplete = job.Status is PdfIngestionStatus.Completed or PdfIngestionStatus.Failed,
                ErrorCode = job.ErrorCode,
                ErrorMessage = job.ErrorMessage,
                RequestedAtUtc = job.CreateDate,
                CompletedAtUtc = job.CompletedDate,
                Verdict = job.Verdict
            };
        }

        private static PdfSignatureValidationAcceptance Rejected(string fileId, string errorCode, string errorMessage) =>
            new()
            {
                FileId = fileId,
                Accepted = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage
            };

        private static PdfIngestionResult<TValue> ValidationFailure<TValue>(
            string errorCode,
            string message,
            string correlationId) =>
            PdfIngestionResult<TValue>.Failure(
                PdfIngestionFailureKind.Validation,
                errorCode,
                message,
                correlationId,
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["fileIds"] = [message] });
    }
}
