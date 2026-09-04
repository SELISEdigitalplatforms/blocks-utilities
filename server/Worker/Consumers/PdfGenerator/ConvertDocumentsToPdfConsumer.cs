using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.Shared.Utilities;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Converts word-processing documents held in storage to PDF, replacing the source file.
    /// </summary>
    /// <remarks>
    /// The PDF is written back to the document's own file ID and the record is renamed to a .pdf
    /// extension, so anything already referencing that ID keeps working and ends up pointing at the
    /// PDF. This is how the e-signature service converts documents before signing, and it is the
    /// reason the command needs nothing but the file ID.
    ///
    /// It is also destructive: the original .docx is gone once this succeeds, so a conversion that
    /// renders badly cannot be compared against the input that produced it. That is the accepted
    /// trade for having one file ID mean one document throughout its life.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfConsumer : IConsumer<ConvertDocumentsToPdfEvent>
    {
        private readonly ILogger<ConvertDocumentsToPdfConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IDocumentToPdfConverter _converter;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public ConvertDocumentsToPdfConsumer(
            ILogger<ConvertDocumentsToPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IDocumentToPdfConverter converter,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _converter = converter;
            _notificationService = notificationService;
        }

        public async Task Consume(ConvertDocumentsToPdfEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation(
                "ConvertDocumentsToPdfConsumer: Processing event for MessageCoRelationId={MessageCoRelationId}, TenantId={TenantId}",
                LogSanitizer.Scrub(@event.MessageCoRelationId),
                LogSanitizer.Scrub(tenantId));

            var successCount = 0;
            var failureCount = 0;

            try
            {
                foreach (var command in @event.ConvertCommands)
                {
                    if (await ConvertOne(command, @event))
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }

                await _notificationService.NotifyConvertDocumentsToPdfEvent(
                    failureCount == 0,
                    @event.MessageCoRelationId,
                    @event.ProjectKey,
                    successCount,
                    failureCount);

                _logger.LogInformation(
                    "ConvertDocumentsToPdfConsumer: Completed processing. Success={SuccessCount}, Failures={FailureCount}",
                    successCount,
                    failureCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ConvertDocumentsToPdfConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}",
                    LogSanitizer.Scrub(@event.MessageCoRelationId));

                await _notificationService.NotifyConvertDocumentsToPdfEvent(
                    false,
                    @event.MessageCoRelationId,
                    @event.ProjectKey,
                    successCount,
                    failureCount);
            }
        }

        /// <summary>
        /// Converts a single document. Returns false rather than throwing, so one unreadable file in
        /// a batch does not abandon the rest of it.
        /// </summary>
        private async Task<bool> ConvertOne(ConvertDocumentToPdfCommand command, ConvertDocumentsToPdfEvent @event)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(command.DocumentFileId))
                {
                    _logger.LogError("ConvertDocumentsToPdfConsumer: DocumentFileId is required");
                    return false;
                }

                // The record first: it carries the name whose extension decides whether this can be
                // converted, and the directory the replacement has to stay in. Resolving it does not
                // transfer the file's bytes, so an unsupported document is rejected before anything
                // large moves.
                var record = await _storageHelper.GetFileRecord(command.DocumentFileId, @event.ProjectKey);
                if (record == null)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: No storage record for DocumentFileId={DocumentFileId}",
                        LogSanitizer.Scrub(command.DocumentFileId));

                    return false;
                }

                var sourceName = record.Name ?? string.Empty;

                _logger.LogInformation(
                    "ConvertDocumentsToPdfConsumer: Converting DocumentFileId={DocumentFileId}, name={DocumentFileName}",
                    LogSanitizer.Scrub(command.DocumentFileId),
                    LogSanitizer.Scrub(sourceName));

                if (!_converter.IsSupportedDocument(sourceName))
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Unsupported document type for name={DocumentFileName}",
                        LogSanitizer.Scrub(sourceName));

                    return false;
                }

                using var documentStream = await _storageHelper.GetStreamForRecord(record);
                if (documentStream == null)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Failed to download DocumentFileId={DocumentFileId}",
                        LogSanitizer.Scrub(command.DocumentFileId));

                    return false;
                }

                var options = new DocumentConversionOptions
                {
                    PreserveFormFields = command.PreserveFormFields,
                    PdfACompliant = command.PdfACompliant,
                    UpdateFields = command.UpdateFields
                };

                using var pdfStream = await _converter.ConvertToPdfAsync(documentStream, options);
                if (pdfStream == null || pdfStream.Length == 0)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Conversion failed for DocumentFileId={DocumentFileId}",
                        LogSanitizer.Scrub(command.DocumentFileId));

                    return false;
                }

                var pdfName = ToPdfName(sourceName, command.DocumentFileId);

                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "ConvertedFromName", sourceName },
                    { "ConvertedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "ConvertedPDF" },
                    { "PdfACompliant", command.PdfACompliant.ToString() }
                };

                // Same item ID and same parent directory: this is a replacement, not a new file. The
                // new name carries the .pdf extension so the record stops claiming to be a Word
                // document once its bytes are a PDF.
                var saveSuccess = await _storageHelper.SavePdfToStorage(
                    pdfStream,
                    command.DocumentFileId,
                    pdfName,
                    metadata,
                    record.ParentDirectoryID ?? string.Empty,
                    @event.ProjectKey);

                if (!saveSuccess)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Failed to write converted PDF back to DocumentFileId={DocumentFileId}",
                        LogSanitizer.Scrub(command.DocumentFileId));

                    return false;
                }

                _logger.LogInformation(
                    "ConvertDocumentsToPdfConsumer: Replaced DocumentFileId={DocumentFileId} with {PdfName}, size={PdfSize} bytes",
                    LogSanitizer.Scrub(command.DocumentFileId),
                    LogSanitizer.Scrub(pdfName),
                    pdfStream.Length);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ConvertDocumentsToPdfConsumer: Error converting DocumentFileId={DocumentFileId}",
                    LogSanitizer.Scrub(command.DocumentFileId));

                return false;
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
