using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class MergePdfsConsumer : IConsumer<MergePdfsEvent>
    {
        private readonly ILogger<MergePdfsConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfEngineProvider _engineProvider;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public MergePdfsConsumer(
            ILogger<MergePdfsConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _engineProvider = engineProvider;
            _notificationService = notificationService;
        }

        public async Task Consume(MergePdfsEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("MergePdfsConsumer: Processing event for OutputPdfFileId={OutputPdfFileId}, TenantId={TenantId}", @event.OutputPdfFileId, tenantId);

            try
            {
                var engine = _engineProvider.GetEngine(@event.Engine);

                // Sort PDFs by order
                var sortedPdfs = @event.PdfFilesToBeMerged.OrderBy(p => p.Order).ToList();
                _logger.LogInformation("MergePdfsConsumer: Merging {Count} PDF files", sortedPdfs.Count);

                // Get all PDF streams
                var pdfStreams = new List<Stream>();
                foreach (var pdfFile in sortedPdfs)
                {
                    var stream = await _storageHelper.GetPdfStream(pdfFile.PdfFileId, @event.ProjectKey);
                    if (stream == null)
                    {
                        _logger.LogError("MergePdfsConsumer: Failed to get PDF stream for PdfFileId={PdfFileId}", pdfFile.PdfFileId);
                        
                        if (@event.HandleCorruptedPdf)
                        {
                            _logger.LogWarning("MergePdfsConsumer: Skipping corrupted PDF PdfFileId={PdfFileId}", pdfFile.PdfFileId);
                            continue;
                        }
                        else
                        {
                            throw new Exception($"Failed to load PDF file: {pdfFile.PdfFileId}");
                        }
                    }
                    pdfStreams.Add(stream);
                }

                if (pdfStreams.Count == 0)
                {
                _logger.LogError("MergePdfsConsumer: No valid PDF streams to merge");
                    await _notificationService.NotifyMergePdfsEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                // Merge PDFs
                _logger.LogInformation("MergePdfsConsumer: Merging {Count} PDF streams", pdfStreams.Count);
                var mergedStream = await engine.MergePdfsAsync(pdfStreams);

                if (mergedStream == null || mergedStream.Length == 0)
                {
                _logger.LogError("MergePdfsConsumer: Failed to merge PDFs");
                    await _notificationService.NotifyMergePdfsEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("MergePdfsConsumer: Successfully merged PDFs, size={PdfSize} bytes", mergedStream.Length);

                // Save to storage
                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "SourcePdfCount", sortedPdfs.Count.ToString() },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "MergedPDF" },
                    { "OpenInBrowser", @event.OpenInBrowser.ToString() }
                };

                var saveSuccess = await _storageHelper.SavePdfToStorage(
                    mergedStream,
                    @event.OutputPdfFileId,
                    @event.OutputPdfFileName,
                    metadata,
                    "Blocks-PDF-Merged-Files",
                    @event.ProjectKey);

                if (!saveSuccess)
                {
                    _logger.LogError("MergePdfsConsumer: Failed to save merged PDF to storage");
                    await _notificationService.NotifyMergePdfsEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("MergePdfsConsumer: Successfully saved merged PDF");

                // Send success notification
                await _notificationService.NotifyMergePdfsEvent(true, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);

                _logger.LogInformation("MergePdfsConsumer: Successfully completed processing for OutputPdfFileId={OutputPdfFileId}", @event.OutputPdfFileId);
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "MergePdfsConsumer: Exception occurred for OutputPdfFileId={OutputPdfFileId}", @event.OutputPdfFileId);
                await _notificationService.NotifyMergePdfsEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
        }
    }
}
