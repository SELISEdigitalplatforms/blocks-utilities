using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class StampTextToPdfConsumer : StampPdfConsumerBase<StampTextToPdfEvent>, IConsumer<StampTextToPdfEvent>
    {
        public StampTextToPdfConsumer(
            ILogger<StampTextToPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
            : base(logger, storageHelper, engineProvider, notificationService)
        {
        }

        protected override string GetConsumerName() => "StampTextToPdfConsumer";

        public async Task Consume(StampTextToPdfEvent @event)
        {
            var (pdfStream, engine) = await InitializeStampingAsync(
                @event.PdfFileId,
                @event.ProjectKey,
                @event.Engine,
                @event.MessageCoRelationId,
                @event.Stamps.Count);

            if (pdfStream == null || engine == null)
            {
                await _notificationService.NotifyStampTextToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                return;
            }

            try
            {
                // Process each stamp
                Stream currentStream = pdfStream;
                foreach (var stamp in @event.Stamps)
                {
                    foreach (var coordinate in stamp.Coordinates)
                    {
                        var stampOptions = new TextStampOptions
                        {
                            Text = stamp.Text,
                            XPosition = coordinate.X,
                            YPosition = coordinate.Y,
                            FontName = stamp.FontName,
                            PageNumbers = new List<int> { coordinate.PageNumber }
                        };

                        _logger.LogInformation("StampTextToPdfConsumer: Stamping text '{Text}' at position ({X}, {Y}) on page {PageNumber}", stamp.Text, coordinate.X, coordinate.Y, coordinate.PageNumber);
                        var stampedStream = await engine.StampTextToPdfAsync(currentStream, stampOptions);

                        if (stampedStream == null || stampedStream.Length == 0)
                        {
                            _logger.LogError("StampTextToPdfConsumer: Failed to stamp text onto PDF");
                            await _notificationService.NotifyStampTextToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                            return;
                        }

                        currentStream = stampedStream;
                    }
                }

                _logger.LogInformation("StampTextToPdfConsumer: Successfully stamped all text, final size={PdfSize} bytes", currentStream.Length);

                // Save stamped PDF
                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "PdfFileId", @event.PdfFileId },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "StampedPDF" },
                    { "StampType", "Text" },
                    { "StampCount", @event.Stamps.Count.ToString() }
                };

                var saveSuccess = await _storageHelper.SavePdfToStorage(
                    currentStream,
                    @event.OutputPdfFileId,
                    @event.OutputPdfFileName,
                    metadata,
                    "Blocks-PDF-Stamped-Files",
                    @event.ProjectKey);

                if (!saveSuccess)
                {
                    _logger.LogError("StampTextToPdfConsumer: Failed to save stamped PDF to storage");
                    await _notificationService.NotifyStampTextToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("StampTextToPdfConsumer: Stamped PDF saved to OutputPdfFileId={OutputPdfFileId}", @event.OutputPdfFileId);

                // Send success notification
                await _notificationService.NotifyStampTextToPdfEvent(true, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StampTextToPdfConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
                await _notificationService.NotifyStampTextToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
        }
    }
}
