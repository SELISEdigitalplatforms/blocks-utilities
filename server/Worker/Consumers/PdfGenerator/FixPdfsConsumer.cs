using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class FixPdfsConsumer : IConsumer<FixPdfsEvent>
    {
        private readonly ILogger<FixPdfsConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfEngineProvider _engineProvider;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public FixPdfsConsumer(
            ILogger<FixPdfsConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _engineProvider = engineProvider;
            _notificationService = notificationService;
        }

        public async Task Consume(FixPdfsEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("FixPdfsConsumer: Processing event for MessageCorrelationId={MessageCorrelationId}, TenantId={TenantId}", @event.MessageCorrelationId, tenantId);

            try
            {
                // Always use engine 1 (Aspose) for PDF repair
                var engine = _engineProvider.GetEngine(1);

                foreach (var fixCommand in @event.PdfInfos)
                {
                    try
                    {
                        _logger.LogInformation("FixPdfsConsumer: Fixing PDF OriginalPdfId={OriginalPdfId}", fixCommand.OriginalPdfId);

                        // Get original PDF
                        var pdfStream = await _storageHelper.GetPdfStream(fixCommand.OriginalPdfId, @event.ProjectKey);
                        if (pdfStream == null)
                        {
                            _logger.LogError("FixPdfsConsumer: Failed to get PDF stream for OriginalPdfId={OriginalPdfId}", fixCommand.OriginalPdfId);
                            continue;
                        }

                        // Fix/repair PDF
                        var fixedStream = await engine.FixPdfAsync(pdfStream);
                        if (fixedStream == null || fixedStream.Length == 0)
                        {
                            _logger.LogError("FixPdfsConsumer: Failed to fix PDF for OriginalPdfId={OriginalPdfId}", fixCommand.OriginalPdfId);
                            continue;
                        }

                        _logger.LogInformation("FixPdfsConsumer: Successfully fixed PDF, size={PdfSize} bytes", fixedStream.Length);

                        // Save fixed PDF
                        var metadata = new Dictionary<string, string>
                        {
                            { "MessageCorrelationId", @event.MessageCorrelationId },
                            { "OriginalPdfId", fixCommand.OriginalPdfId },
                            { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "FileType", "FixedPDF" }
                        };

                        var saveSuccess = await _storageHelper.SavePdfToStorage(
                            fixedStream,
                            fixCommand.OutputPdfId,
                            $"{fixCommand.OutputPdfId}_fixed.pdf",
                            metadata,
                            "Blocks-PDF-Fixed-Files",
                            @event.ProjectKey);

                        if (saveSuccess)
                        {
                            _logger.LogInformation("FixPdfsConsumer: Successfully saved fixed PDF for OutputPdfId={OutputPdfId}", fixCommand.OutputPdfId);
                        }
                        else
                        {
                            _logger.LogError("FixPdfsConsumer: Failed to save fixed PDF for OutputPdfId={OutputPdfId}", fixCommand.OutputPdfId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FixPdfsConsumer: Error fixing PDF OriginalPdfId={OriginalPdfId}", fixCommand.OriginalPdfId);
                    }
                }

                await _notificationService.NotifyFixPdfsEvent(true, @event.MessageCorrelationId, @event.ProjectKey);
                _logger.LogInformation("FixPdfsConsumer: Successfully completed processing");
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "FixPdfsConsumer: Exception occurred for MessageCorrelationId={MessageCorrelationId}", @event.MessageCorrelationId);
                await _notificationService.NotifyFixPdfsEvent(false, @event.MessageCorrelationId, @event.ProjectKey);
            }
        }
    }
}
