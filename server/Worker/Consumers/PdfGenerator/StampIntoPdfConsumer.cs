using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class StampIntoPdfConsumer : StampPdfConsumerBase<StampIntoPdfEvent>, IConsumer<StampIntoPdfEvent>
    {
        public StampIntoPdfConsumer(
            ILogger<StampIntoPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
            : base(logger, storageHelper, engineProvider, notificationService)
        {
        }

        protected override string GetConsumerName() => "StampIntoPdfConsumer";

        public async Task Consume(StampIntoPdfEvent @event)
        {
            var (pdfStream, engine) = await InitializeStampingAsync(
                @event.PdfFileId,
                @event.ProjectKey,
                @event.Engine,
                @event.MessageCoRelationId,
                @event.Stamps.Count);

            if (pdfStream == null || engine == null)
            {
                await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
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
                        // Check stamp type (0 = Image, 1 = Text)
                        if (stamp.Type == 0 && !string.IsNullOrEmpty(stamp.ImageFileId))
                        {
                            // Image stamp
                            var imageStream = await _storageHelper.GetPdfStream(stamp.ImageFileId, @event.ProjectKey);
                            if (imageStream == null)
                            {
                                _logger.LogError("StampIntoPdfConsumer: Failed to get image stream for ImageFileId={ImageFileId}", stamp.ImageFileId);
                                await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                                return;
                            }

                            var imageOptions = new ImageStampOptions
                            {
                                XPosition = coordinate.X,
                                YPosition = coordinate.Y,
                                Width = coordinate.Width,
                                Height = coordinate.Height,
                                PageNumbers = new List<int> { coordinate.PageNumber }
                            };

                            _logger.LogInformation("StampIntoPdfConsumer: Stamping image at position ({X}, {Y}) on page {PageNumber}", coordinate.X, coordinate.Y, coordinate.PageNumber);
                            var stampedStream = await engine.StampImageToPdfAsync(currentStream, imageStream, imageOptions);

                            if (stampedStream == null || stampedStream.Length == 0)
                            {
                                _logger.LogError("StampIntoPdfConsumer: Failed to stamp image onto PDF");
                                await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                                return;
                            }

                            currentStream = stampedStream;
                        }
                        else if (stamp.Type == 1 && !string.IsNullOrEmpty(stamp.Text))
                        {
                            // Text stamp
                            var textOptions = new TextStampOptions
                            {
                                Text = stamp.Text,
                                XPosition = coordinate.X,
                                YPosition = coordinate.Y,
                                FontName = stamp.FontName,
                                PageNumbers = new List<int> { coordinate.PageNumber }
                            };

                            _logger.LogInformation("StampIntoPdfConsumer: Stamping text '{Text}' at position ({X}, {Y}) on page {PageNumber}", stamp.Text, coordinate.X, coordinate.Y, coordinate.PageNumber);
                            var stampedStream = await engine.StampTextToPdfAsync(currentStream, textOptions);

                            if (stampedStream == null || stampedStream.Length == 0)
                            {
                                _logger.LogError("StampIntoPdfConsumer: Failed to stamp text onto PDF");
                                await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                                return;
                            }

                            currentStream = stampedStream;
                        }
                    }
                }

                _logger.LogInformation("StampIntoPdfConsumer: Successfully stamped all items, final size={PdfSize} bytes", currentStream.Length);

                // Save stamped PDF
                var metadata = new Dictionary<string, string>
                {
                    { "MessageCoRelationId", @event.MessageCoRelationId },
                    { "PdfFileId", @event.PdfFileId },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "StampedPDF" },
                    { "StampType", "Mixed" },
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
                    _logger.LogError("StampIntoPdfConsumer: Failed to save stamped PDF to storage");
                    await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("StampIntoPdfConsumer: Stamped PDF saved to OutputPdfFileId={OutputPdfFileId}", @event.OutputPdfFileId);

                // Send success notification
                await _notificationService.NotifyStampIntoPdfEvent(true, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StampIntoPdfConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
                await _notificationService.NotifyStampIntoPdfEvent(false, @event.OutputPdfFileId, @event.MessageCoRelationId, @event.ProjectKey);
            }
        }
    }
}
