using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class DocumentConversionService : IDocumentConversionService
    {
        /// <summary>
        /// The most files one request may name. A caller fanning out an unbounded list in a single
        /// call turns one HTTP request into an unbounded number of Mongo writes and queue publishes
        /// before anything is returned; this keeps that work, and the time a caller waits for it,
        /// bounded to something a single request should reasonably ask for.
        /// </summary>
        internal const int MaxBatchSize = 50;

        private readonly ILogger<DocumentConversionService> _logger;
        private readonly IMessageClient _messageClient;
        private readonly IPdfGeneratorRepository _repository;
        private readonly PdfStorageHelper _storageHelper;

        public DocumentConversionService(
            ILogger<DocumentConversionService> logger,
            IMessageClient messageClient,
            IPdfGeneratorRepository repository,
            PdfStorageHelper storageHelper)
        {
            _logger = logger;
            _messageClient = messageClient;
            _repository = repository;
            _storageHelper = storageHelper;
        }

        /// <inheritdoc />
        public async Task<DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>> RequestConversionsAsync(
            ConvertDocumentToPdfRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // A duplicate ID is one request for that file, not two, so queuing it twice would just
            // race two workers over the same replacement.
            var fileIds = (request.FileIds ?? new List<string>())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fileIds.Count == 0)
            {
                return DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
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
                return DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
                    "too_many_files",
                    $"A single request can convert at most {MaxBatchSize} files; {fileIds.Count} were sent.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = [$"A single request can convert at most {MaxBatchSize} files."]
                    });
            }

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var results = new List<DocumentConversionAcceptance>(fileIds.Count);

            // Sequential rather than fanned out with Task.WhenAll: each file writes to the same
            // tenant database and publishes to the same queue, so nothing here benefits from
            // concurrency the way the read side's storage lookups do, and staying sequential keeps
            // the per-file logging in request order.
            foreach (var fileId in fileIds)
            {
                results.Add(await RequestOneConversion(fileId, request.MessageCoRelationId, tenantId));
            }

            var accepted = results.Count(r => r.Accepted);

            return DocumentConversionResult<ConvertDocumentsToPdfBatchResponse>.Success(
                new ConvertDocumentsToPdfBatchResponse
                {
                    MessageCoRelationId = request.MessageCoRelationId,
                    Results = results,
                    AcceptedCount = accepted,
                    RejectedCount = results.Count - accepted
                },
                correlationId);
        }

        /// <summary>
        /// Records and queues the conversion of one file. Never throws — any failure becomes a
        /// rejected <see cref="DocumentConversionAcceptance"/>, so one bad file cannot abort the rest
        /// of the batch the caller is waiting on.
        /// </summary>
        private async Task<DocumentConversionAcceptance> RequestOneConversion(
            string fileId,
            string? messageCoRelationId,
            string tenantId)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return new DocumentConversionAcceptance
                {
                    FileId = fileId ?? string.Empty,
                    Accepted = false,
                    ErrorCode = "input_file_id_required",
                    ErrorMessage = "A file ID in the list was blank."
                };
            }

            var now = DateTime.UtcNow;

            var job = new DocumentConversionJob
            {
                Id = fileId,
                MessageCoRelationId = messageCoRelationId,
                Status = DocumentConversionStatus.Queued,
                TenantId = tenantId,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreateDate = now,
                LastUpdateDate = now
            };

            // The record is written before the event is published, deliberately. A caller that gets
            // an acknowledgement back must be able to poll immediately; publishing first leaves a
            // window where the worker has already finished and the status endpoint still says the
            // file was never submitted.
            if (!await _repository.SaveDocumentConversionJobAsync(job))
            {
                return new DocumentConversionAcceptance
                {
                    FileId = fileId,
                    Accepted = false,
                    ErrorCode = "conversion_not_recorded",
                    ErrorMessage = "The conversion could not be recorded and was not started."
                };
            }

            try
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<ConvertDocumentToPdfEvent>
                    {
                        ConsumerName = PdfGeneratorConstants.ConvertDocumentToPdfQueue,
                        Payload = new ConvertDocumentToPdfEvent
                        {
                            FileId = fileId,
                            MessageCoRelationId = messageCoRelationId,
                            ProjectKey = tenantId
                        }
                    });
            }
            catch (Exception ex)
            {
                // The record exists but nothing will ever pick it up, so it is failed here rather
                // than left Queued forever — a poller deserves an answer, not a permanent maybe.
                _logger.LogError(
                    ex,
                    "RequestConversionsAsync: Failed to queue conversion of file {FileId}",
                    LogSanitizer.Scrub(fileId));

                job.Status = DocumentConversionStatus.Failed;
                job.ErrorCode = "conversion_not_queued";
                job.ErrorMessage = "The conversion could not be queued.";
                job.CompletedDate = DateTime.UtcNow;
                await _repository.UpdateDocumentConversionJobAsync(job);

                return new DocumentConversionAcceptance
                {
                    FileId = fileId,
                    Accepted = false,
                    ErrorCode = "conversion_not_queued",
                    ErrorMessage = "The conversion could not be queued."
                };
            }

            _logger.LogInformation(
                "RequestConversionsAsync: Queued conversion of file {FileId}",
                LogSanitizer.Scrub(fileId));

            return new DocumentConversionAcceptance
            {
                FileId = fileId,
                Accepted = true,
                Status = job.Status
            };
        }

        /// <inheritdoc />
        public async Task<DocumentConversionResult<DocumentConversionStatusBatchResponse>> GetStatusAsync(
            GetDocumentConversionStatusRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var fileIds = (request.FileIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (fileIds.Count == 0)
            {
                return DocumentConversionResult<DocumentConversionStatusBatchResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
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
                return DocumentConversionResult<DocumentConversionStatusBatchResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
                    "too_many_files",
                    $"A single request can query at most {MaxBatchSize} files; {fileIds.Count} were sent.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["fileIds"] = [$"A single request can query at most {MaxBatchSize} files."]
                    });
            }

            var jobs = await _repository.GetDocumentConversionJobsAsync(fileIds);
            var jobsById = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);

            // Storage is consulted once per succeeded file, and those lookups are independent of one
            // another, so they run concurrently rather than serially the way the write side does —
            // there is no shared record any of them is racing to update.
            var resolutions = await Task.WhenAll(fileIds.Select(fileId => BuildStatus(fileId, jobsById)));

            return DocumentConversionResult<DocumentConversionStatusBatchResponse>.Success(
                new DocumentConversionStatusBatchResponse { Results = resolutions.ToList() },
                correlationId);
        }

        private async Task<DocumentConversionStatusResult> BuildStatus(
            string fileId,
            IReadOnlyDictionary<string, DocumentConversionJob> jobsById)
        {
            if (!jobsById.TryGetValue(fileId, out var job))
            {
                return new DocumentConversionStatusResult
                {
                    FileId = fileId,
                    Found = false,
                    ErrorCode = "conversion_not_found",
                    ErrorMessage = "That file has not been submitted for conversion."
                };
            }

            var isComplete = job.Status is DocumentConversionStatus.Succeeded or DocumentConversionStatus.Failed;

            var result = new DocumentConversionStatusResult
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

            if (job.Status == DocumentConversionStatus.Succeeded)
            {
                // Resolved per request rather than stored: a storage URL is time-limited, so one
                // written at conversion time would be expired by the time a poller asked for it.
                var record = await _storageHelper.GetFileRecord(job.Id);

                if (record == null)
                {
                    _logger.LogWarning(
                        "GetStatusAsync: Conversion of file {FileId} succeeded but the file could not be resolved",
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
