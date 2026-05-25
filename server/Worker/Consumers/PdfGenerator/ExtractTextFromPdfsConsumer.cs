using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Entities;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class ExtractTextFromPdfsConsumer : IConsumer<ExtractTextFromPdfsEvent>
    {
        private readonly ILogger<ExtractTextFromPdfsConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfEngineProvider _engineProvider;
        private readonly IPdfGeneratorRepository _repository;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public ExtractTextFromPdfsConsumer(
            ILogger<ExtractTextFromPdfsConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorRepository repository,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _engineProvider = engineProvider;
            _repository = repository;
            _notificationService = notificationService;
        }

        public async Task Consume(ExtractTextFromPdfsEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("ExtractTextFromPdfsConsumer: Processing event for MessageCoRelationId={MessageCoRelationId}, TenantId={TenantId}", @event.MessageCoRelationId, tenantId);

            try
            {
                var engine = _engineProvider.GetEngine(@event.Engine);

                foreach (var extractCommand in @event.ExtractTextCommands)
                {
                    try
                    {
                        _logger.LogInformation("ExtractTextFromPdfsConsumer: Processing PdfFileId={PdfFileId}, RecordId={RecordId}", extractCommand.PdfFileId, extractCommand.RecordId);

                        // Get PDF file from storage
                        var pdfStream = await _storageHelper.GetPdfStream(extractCommand.PdfFileId, @event.ProjectKey);
                        if (pdfStream == null)
                        {
                            _logger.LogError("ExtractTextFromPdfsConsumer: Failed to get PDF stream for PdfFileId={PdfFileId}", extractCommand.PdfFileId);
                            continue;
                        }

                        // Extract text using PDF engine
                        var extractedText = await engine.ExtractTextFromPdfAsync(pdfStream);
                        if (string.IsNullOrEmpty(extractedText))
                        {
                            _logger.LogWarning("ExtractTextFromPdfsConsumer: No text extracted from PdfFileId={PdfFileId}", extractCommand.PdfFileId);
                            extractedText = string.Empty;
                        }

                        _logger.LogInformation("ExtractTextFromPdfsConsumer: Extracted {CharCount} characters from PDF", extractedText.Length);

                        // Save to database
                        var pdfExtractDump = new PdfExtractDump
                        {
                            Text = extractedText,
                            ItemId = extractCommand.RecordId,
                            MessageCorrelationId = @event.MessageCoRelationId,
                            PdfId = extractCommand.PdfFileId,
                            CreateDate = DateTime.UtcNow,
                            CreatedBy = BlocksContext.GetContext()?.UserId ?? "system",
                            LastUpdateDate = DateTime.UtcNow,
                            LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system",
                            Tags = new[] { "PdfExtract" },
                            TenantId = tenantId
                        };

                        var saved = await _repository.SavePdfExtractDumpAsync(pdfExtractDump, tenantId);
                        if (saved)
                        {
                            _logger.LogInformation("ExtractTextFromPdfsConsumer: Successfully saved extract dump for RecordId={RecordId}", extractCommand.RecordId);
                        }
                        else
                        {
                            _logger.LogError("ExtractTextFromPdfsConsumer: Failed to save extract dump for RecordId={RecordId}", extractCommand.RecordId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "ExtractTextFromPdfsConsumer: Error processing PdfFileId={PdfFileId}", extractCommand.PdfFileId);
                    }
                }

                // Send notification
                await _notificationService.NotifyExtractTextFromPdfsEvent(true, @event.MessageCoRelationId, @event.ProjectKey);

                _logger.LogInformation("ExtractTextFromPdfsConsumer: Successfully completed processing for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "ExtractTextFromPdfsConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
                await _notificationService.NotifyExtractTextFromPdfsEvent(false, @event.MessageCoRelationId, @event.ProjectKey);
            }
        }
    }
}
