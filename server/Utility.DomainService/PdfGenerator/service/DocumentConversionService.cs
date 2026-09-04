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

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var now = DateTime.UtcNow;

            var job = new DocumentConversionJob
            {
                Id = Guid.NewGuid().ToString(),
                InputFileId = request.InputFileId,
                MessageCoRelationId = request.MessageCoRelationId,
                Status = DocumentConversionStatus.Queued,
                TenantId = tenantId,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreateDate = now,
                LastUpdateDate = now
            };

            // The record is written before the event is published, deliberately. A caller that gets
            // a conversion ID back must be able to look it up immediately; publishing first leaves a
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
                            ConversionId = job.Id,
                            InputFileId = job.InputFileId,
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
                    "RequestConversionAsync: Failed to queue conversion {ConversionId}",
                    job.Id);

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
                "RequestConversionAsync: Queued conversion {ConversionId} for InputFileId={InputFileId}",
                job.Id,
                LogSanitizer.Scrub(job.InputFileId));

            return DocumentConversionResult<ConvertDocumentToPdfAcceptedResponse>.Success(
                new ConvertDocumentToPdfAcceptedResponse
                {
                    ConversionId = job.Id,
                    InputFileId = job.InputFileId,
                    MessageCoRelationId = job.MessageCoRelationId,
                    Status = job.Status,
                    StatusUrl = $"/document-conversions/{job.Id}"
                },
                correlationId);
        }

        /// <inheritdoc />
        public async Task<DocumentConversionResult<DocumentConversionStatusResponse>> GetStatusAsync(
            string conversionId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(conversionId))
            {
                return DocumentConversionResult<DocumentConversionStatusResponse>.Failure(
                    DocumentConversionFailureKind.Validation,
                    "conversion_id_required",
                    "conversionId is required.",
                    correlationId);
            }

            var job = await _repository.GetDocumentConversionJobAsync(conversionId);

            if (job == null)
            {
                return DocumentConversionResult<DocumentConversionStatusResponse>.Failure(
                    DocumentConversionFailureKind.NotFound,
                    "conversion_not_found",
                    "No conversion with that ID.",
                    correlationId);
            }

            var isComplete = job.Status is DocumentConversionStatus.Succeeded or DocumentConversionStatus.Failed;

            var response = new DocumentConversionStatusResponse
            {
                ConversionId = job.Id,
                InputFileId = job.InputFileId,
                MessageCoRelationId = job.MessageCoRelationId,
                Status = job.Status,
                IsComplete = isComplete,
                SourceFileName = job.SourceFileName,
                ErrorCode = job.ErrorCode,
                ErrorMessage = job.ErrorMessage,
                RequestedAtUtc = job.CreateDate,
                CompletedAtUtc = job.CompletedDate
            };

            if (job.Status == DocumentConversionStatus.Succeeded)
            {
                response.FileId = job.InputFileId;
                response.FileName = job.ConvertedFileName;

                // Resolved per request rather than stored: a storage URL is time-limited, so one
                // written at conversion time would be expired by the time a poller asked for it.
                var record = await _storageHelper.GetFileRecord(job.InputFileId);
                if (record == null)
                {
                    _logger.LogWarning(
                        "GetStatusAsync: Conversion {ConversionId} succeeded but its file could not be resolved",
                        job.Id);
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
