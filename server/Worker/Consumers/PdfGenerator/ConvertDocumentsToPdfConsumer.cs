using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Converts word-processing documents held in storage to PDF and writes the result back as a
    /// new file.
    /// </summary>
    /// <remarks>
    /// The source file is left untouched and the PDF is written to its own file ID, unlike the
    /// e-signature service's converter which overwrites the original and renames it. Keeping both
    /// means a conversion that renders badly can be diagnosed against the input that produced it,
    /// and it matches how every other consumer in this module treats its output.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public class ConvertDocumentsToPdfConsumer : IConsumer<ConvertDocumentsToPdfEvent>
    {
        /// <summary>
        /// Where converted PDFs land. Its own directory so a retention or access policy can be set
        /// for converted source documents without touching generated output.
        /// </summary>
        private const string OutputDirectory = "Blocks-PDF-Converted-Files";

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
                @event.MessageCoRelationId,
                tenantId);

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
                    @event.MessageCoRelationId);

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
                _logger.LogInformation(
                    "ConvertDocumentsToPdfConsumer: Converting DocumentFileId={DocumentFileId}, name={DocumentFileName}",
                    command.DocumentFileId,
                    command.DocumentFileName);

                // Checked before the download: an unsupported extension is a caller error, and
                // finding that out after pulling the bytes across the network costs the same
                // rejection plus a transfer.
                if (!_converter.IsSupportedDocument(command.DocumentFileName))
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Unsupported document type for DocumentFileName={DocumentFileName}",
                        command.DocumentFileName);

                    return false;
                }

                using var documentStream = await _storageHelper.GetPdfStream(command.DocumentFileId, @event.ProjectKey);
                if (documentStream == null)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Failed to get document stream for DocumentFileId={DocumentFileId}",
                        command.DocumentFileId);

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
                        command.DocumentFileId);

                    return false;
                }

                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "SourceDocumentFileId", command.DocumentFileId },
                    { "SourceDocumentFileName", command.DocumentFileName },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "ConvertedPDF" },
                    { "PdfACompliant", command.PdfACompliant.ToString() },
                    { "OpenInBrowser", command.OpenInBrowser.ToString() }
                };

                var saveSuccess = await _storageHelper.SavePdfToStorage(
                    pdfStream,
                    command.OutputPdfFileId,
                    ResolveOutputFileName(command),
                    metadata,
                    OutputDirectory,
                    @event.ProjectKey);

                if (!saveSuccess)
                {
                    _logger.LogError(
                        "ConvertDocumentsToPdfConsumer: Failed to save converted PDF for OutputPdfFileId={OutputPdfFileId}",
                        command.OutputPdfFileId);

                    return false;
                }

                _logger.LogInformation(
                    "ConvertDocumentsToPdfConsumer: Saved converted PDF for OutputPdfFileId={OutputPdfFileId}, size={PdfSize} bytes",
                    command.OutputPdfFileId,
                    pdfStream.Length);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ConvertDocumentsToPdfConsumer: Error converting DocumentFileId={DocumentFileId}",
                    command.DocumentFileId);

                return false;
            }
        }

        /// <summary>
        /// Uses the caller's output name when given, otherwise the source name with its extension
        /// swapped for .pdf — the same naming the e-signature converter produces.
        /// </summary>
        public static string ResolveOutputFileName(ConvertDocumentToPdfCommand command)
        {
            if (!string.IsNullOrWhiteSpace(command.OutputPdfFileName))
            {
                return command.OutputPdfFileName;
            }

            var withoutExtension = Path.GetFileNameWithoutExtension(command.DocumentFileName);

            return string.IsNullOrWhiteSpace(withoutExtension)
                ? $"{command.OutputPdfFileId}.pdf"
                : $"{withoutExtension}.pdf";
        }
    }
}
