using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Shared.Utilities;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Converts one word-processing document held in storage to PDF, replacing the source file.
    /// </summary>
    /// <remarks>
    /// The PDF is written back to the document's own file ID and the record is renamed to a .pdf
    /// extension, so anything already referencing that ID keeps working and ends up pointing at the
    /// PDF. This is how the e-signature service converts documents before signing.
    ///
    /// It is also destructive: the original .docx is gone once this succeeds, so a conversion that
    /// renders badly cannot be compared against the input that produced it. That is the accepted
    /// trade for one file ID meaning one document throughout its life.
    ///
    /// Every exit path writes the conversion record. The completion notification can be missed, and
    /// the record is the only thing the status endpoint can answer from — a path that returns
    /// without updating it strands a caller polling <c>Queued</c> forever.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public class ConvertDocumentToPdfConsumer : IConsumer<ConvertDocumentToPdfEvent>
    {
        private readonly ILogger<ConvertDocumentToPdfConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IDocumentToPdfConverter _converter;
        private readonly IPdfGeneratorRepository _repository;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public ConvertDocumentToPdfConsumer(
            ILogger<ConvertDocumentToPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IDocumentToPdfConverter converter,
            IPdfGeneratorRepository repository,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _converter = converter;
            _repository = repository;
            _notificationService = notificationService;
        }

        public async Task Consume(ConvertDocumentToPdfEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";

            _logger.LogInformation(
                "ConvertDocumentToPdfConsumer: Processing conversion {ConversionId} for InputFileId={InputFileId}, TenantId={TenantId}",
                LogSanitizer.Scrub(@event.ConversionId),
                LogSanitizer.Scrub(@event.InputFileId),
                LogSanitizer.Scrub(tenantId));

            var job = await _repository.GetDocumentConversionJobAsync(@event.ConversionId, @event.ProjectKey);

            if (job == null)
            {
                // Nothing to report progress against, and no caller can be polling for it. Running
                // the conversion anyway would replace a file with no record of why.
                _logger.LogError(
                    "ConvertDocumentToPdfConsumer: No conversion record {ConversionId}; skipping",
                    LogSanitizer.Scrub(@event.ConversionId));

                return;
            }

            try
            {
                job.Status = DocumentConversionStatus.Processing;
                await _repository.UpdateDocumentConversionJobAsync(job, @event.ProjectKey);

                await ConvertAsync(job, @event);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ConvertDocumentToPdfConsumer: Conversion {ConversionId} threw",
                    LogSanitizer.Scrub(@event.ConversionId));

                await Fail(job, @event, "conversion_error", "The conversion failed unexpectedly.");
            }
        }

        private async Task ConvertAsync(DocumentConversionJob job, ConvertDocumentToPdfEvent @event)
        {
            // The record first: it carries the name whose extension decides whether this can be
            // converted, and the directory the replacement has to stay in. Resolving it does not
            // transfer the file's bytes, so an unsupported document is rejected before anything
            // large moves.
            var record = await _storageHelper.GetFileRecord(job.InputFileId, @event.ProjectKey);

            if (record == null)
            {
                await Fail(job, @event, "input_file_not_found", "The document could not be found in storage.");
                return;
            }

            var sourceName = record.Name ?? string.Empty;
            job.SourceFileName = sourceName;

            if (!_converter.IsSupportedDocument(sourceName))
            {
                await Fail(
                    job,
                    @event,
                    "unsupported_document_type",
                    $"'{sourceName}' is not a document type that can be converted.");

                return;
            }

            using var documentStream = await _storageHelper.GetStreamForRecord(record);

            if (documentStream == null)
            {
                await Fail(job, @event, "input_file_unreadable", "The document could not be downloaded.");
                return;
            }

            using var pdfStream = await _converter.ConvertToPdfAsync(documentStream, new DocumentConversionOptions());

            if (pdfStream == null || pdfStream.Length == 0)
            {
                await Fail(job, @event, "conversion_failed", "The document could not be rendered to PDF.");
                return;
            }

            var pdfName = ToPdfName(sourceName, job.InputFileId);

            var metadata = new Dictionary<string, string>
            {
                { "ConversionId", job.Id },
                { "ConvertedFromName", sourceName },
                { "ConvertedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                { "FileType", "ConvertedPDF" }
            };

            // Same item ID and same parent directory: this is a replacement, not a new file. The new
            // name carries the .pdf extension so the record stops claiming to be a Word document
            // once its bytes are a PDF.
            var saved = await _storageHelper.SavePdfToStorage(
                pdfStream,
                job.InputFileId,
                pdfName,
                metadata,
                record.ParentDirectoryID ?? string.Empty,
                @event.ProjectKey);

            if (!saved)
            {
                await Fail(job, @event, "output_not_saved", "The converted PDF could not be written back to storage.");
                return;
            }

            job.Status = DocumentConversionStatus.Succeeded;
            job.ConvertedFileName = pdfName;
            job.CompletedDate = DateTime.UtcNow;
            await _repository.UpdateDocumentConversionJobAsync(job, @event.ProjectKey);

            _logger.LogInformation(
                "ConvertDocumentToPdfConsumer: Conversion {ConversionId} replaced InputFileId={InputFileId} with {PdfName}, size={PdfSize} bytes",
                LogSanitizer.Scrub(job.Id),
                LogSanitizer.Scrub(job.InputFileId),
                LogSanitizer.Scrub(pdfName),
                pdfStream.Length);

            await Notify(job, @event, success: true);
        }

        private async Task Fail(
            DocumentConversionJob job,
            ConvertDocumentToPdfEvent @event,
            string errorCode,
            string errorMessage)
        {
            _logger.LogError(
                "ConvertDocumentToPdfConsumer: Conversion {ConversionId} failed: {ErrorCode}",
                LogSanitizer.Scrub(job.Id),
                errorCode);

            job.Status = DocumentConversionStatus.Failed;
            job.ErrorCode = errorCode;
            job.ErrorMessage = errorMessage;
            job.CompletedDate = DateTime.UtcNow;

            await _repository.UpdateDocumentConversionJobAsync(job, @event.ProjectKey);
            await Notify(job, @event, success: false);
        }

        /// <summary>
        /// Sends the completion notification. Never lets a notification failure change the recorded
        /// outcome — the record is already written, and the status endpoint exists precisely because
        /// this call cannot be relied on.
        /// </summary>
        private async Task Notify(DocumentConversionJob job, ConvertDocumentToPdfEvent @event, bool success)
        {
            try
            {
                await _notificationService.NotifyConvertDocumentToPdfEvent(
                    success,
                    job.Id,
                    job.MessageCoRelationId ?? string.Empty,
                    @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "ConvertDocumentToPdfConsumer: Could not notify for conversion {ConversionId}; status endpoint still has the outcome",
                    LogSanitizer.Scrub(job.Id));
            }
        }

        /// <summary>
        /// The source name with its extension swapped for .pdf, falling back to the file ID when
        /// storage holds no usable name.
        /// </summary>
        public static string ToPdfName(string? sourceName, string fileId)
        {
            var withoutExtension = string.IsNullOrWhiteSpace(sourceName)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(sourceName);

            return string.IsNullOrWhiteSpace(withoutExtension)
                ? $"{fileId}.pdf"
                : $"{withoutExtension}.pdf";
        }
    }
}
