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
        public async Task<DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>> RequestConversionAsync(
            ConvertDocumentToPdfRequest request,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.InputFileId))
            {
                return DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
                    "input_file_id_required",
                    "inputFileId is required.",
                    correlationId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["inputFileId"] = ["inputFileId is required."]
                    });
            }

            var fileId = request.InputFileId;
            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var now = DateTime.UtcNow;

            var job = new DocumentConversionJob
            {
                Id = fileId,
                MessageCoRelationId = request.MessageCoRelationId,
                Status = DocumentConversionStatus.Queued,
                TenantId = tenantId,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreateDate = now,
                LastUpdateDate = now
            };

            // The record is written before the event is published, deliberately. A caller that gets
            // an acknowledgement back must be able to poll immediately; publishing first leaves a
            // window where the worker has already finished and the status endpoint still 404s.
            if (!await _repository.SaveDocumentConversionJobAsync(job))
            {
                return DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Failure(
                    DocumentConversionFailureKind.Unavailable,
                    "conversion_not_recorded",
                    "The conversion could not be recorded and was not started.",
                    correlationId);
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
                            MessageCoRelationId = job.MessageCoRelationId,
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
                    "RequestConversionAsync: Failed to queue conversion of file {FileId}",
                    LogSanitizer.Scrub(fileId));

                job.Status = DocumentConversionStatus.Failed;
                job.ErrorCode = "conversion_not_queued";
                job.ErrorMessage = "The conversion could not be queued.";
                job.CompletedDate = DateTime.UtcNow;
                await _repository.UpdateDocumentConversionJobAsync(job);

                return DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Failure(
                    DocumentConversionFailureKind.Unavailable,
                    "conversion_not_queued",
                    "The conversion could not be queued.",
                    correlationId);
            }

            _logger.LogInformation(
                "RequestConversionAsync: Queued conversion of file {FileId}",
                LogSanitizer.Scrub(fileId));

            return DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Success(
                new ConvertDocumentToPdfAcceptedResponse
                {
                    FileId = fileId,
                    MessageCoRelationId = job.MessageCoRelationId,
                    Status = job.Status,
                    StatusUrl = $"/document-conversions/{fileId}"
                },
                correlationId);
        }

        /// <inheritdoc />
        public async Task<DocumentConversionResult<DocumentConversionStatusResponse>> GetStatusAsync(
            string fileId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return DocumentConversionResult<DocumentConversionStatusResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
                    "file_id_required",
                    "fileId is required.",
                    correlationId);
            }

            var job = await _repository.GetDocumentConversionJobAsync(fileId);

            if (job == null)
            {
                return DocumentConversionResult<DocumentConversionStatusResponse>.Failure(
                    DocumentConversionFailureKind.NotFound,
                    "conversion_not_found",
                    "That file has not been submitted for conversion.",
                    correlationId);
            }

            var isComplete = job.Status is DocumentConversionStatus.Succeeded or DocumentConversionStatus.Failed;

            var response = new DocumentConversionStatusResponse
            {
                FileId = job.Id,
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
                    response.DownloadUrl = record.Url;
                    response.FileName ??= record.Name;
                }
            }

            return DocumentConversionResult<DocumentConversionStatusResponse>.Success(response, correlationId);
        }
    }
}
