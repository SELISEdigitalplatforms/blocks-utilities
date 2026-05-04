using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class StampImageToPdfConsumer : StampPdfConsumerBase<StampImageToPdfEvent>, IConsumer<StampImageToPdfEvent>
    {
        public StampImageToPdfConsumer(
            ILogger<StampImageToPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
            : base(logger, storageHelper, engineProvider, notificationService)
        {
        }

        protected override string GetConsumerName() => "StampImageToPdfConsumer";

        public async Task Consume(StampImageToPdfEvent @event)
        {
            var (pdfStream, engine) = await InitializeStampingAsync(
                @event.PdfFileId,
                @event.ProjectKey,
                @event.Engine,
                @event.MessageCoRelationId,
                @event.Stamps.Count);

            if (pdfStream == null || engine == null)
            {
                await _notificationService.NotifyStampImageToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                return;
            }

            try
            {
                // Process each stamp
                Stream currentStream = pdfStream;
                foreach (var stamp in @event.Stamps)
                {
                    // Get image stream
                    var imageStream = await _storageHelper.GetPdfStream(stamp.ImageFileId, @event.ProjectKey);
                    if (imageStream == null)
                    {
                        _logger.LogError("StampImageToPdfConsumer: Failed to get image stream for ImageFileId={ImageFileId}", stamp.ImageFileId);
                        await _notificationService.NotifyStampImageToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                        return;
                    }

                    foreach (var coordinate in stamp.Coordinates)
                    {
                        var stampOptions = new ImageStampOptions
                        {
                            XPosition = coordinate.X,
                            YPosition = coordinate.Y,
                            Width = coordinate.Width,
                            Height = coordinate.Height,
                            PageNumbers = new List<int> { coordinate.PageNumber }
                        };

                        _logger.LogInformation("StampImageToPdfConsumer: Stamping image at position ({X}, {Y}) on page {PageNumber}", coordinate.X, coordinate.Y, coordinate.PageNumber);
                        var stampedStream = await engine.StampImageToPdfAsync(currentStream, imageStream, stampOptions);

                        if (stampedStream == null || stampedStream.Length == 0)
                        {
                            _logger.LogError("StampImageToPdfConsumer: Failed to stamp image onto PDF");
                            await _notificationService.NotifyStampImageToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                            return;
                        }

                        currentStream = stampedStream;
                    }
                }

                _logger.LogInformation("StampImageToPdfConsumer: Successfully stamped all images, final size={PdfSize} bytes", currentStream.Length);

                // Save stamped PDF
                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "PdfFileId", @event.PdfFileId },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "StampedPDF" },
                    { "StampType", "Image" },
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
                    _logger.LogError("StampImageToPdfConsumer: Failed to save stamped PDF to storage");
                    await _notificationService.NotifyStampImageToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("StampImageToPdfConsumer: Stamped PDF saved to OutputPdfFileId={OutputPdfFileId}", @event.OutputPdfFileId);

                // Send success notification
                await _notificationService.NotifyStampImageToPdfEvent(true, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StampImageToPdfConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
                await _notificationService.NotifyStampImageToPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
        }
    }
}
