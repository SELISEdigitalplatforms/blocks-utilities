using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Service implementation for PDF generator operations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfGeneratorService : IPdfGeneratorService
    {
        private readonly ILogger<PdfGeneratorService> _logger;
        private readonly IMessageClient _messageClient;

        public PdfGeneratorService(
            ILogger<PdfGeneratorService> logger,
            IMessageClient messageClient)
        {
            _logger = logger;
            _messageClient = messageClient;
        }

        public async Task<MergePdfsResponse> MergePdfsAsync(MergePdfsRequest request)
        {
            try
            {
                _logger.LogInformation("MergePdfsAsync started for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                // Send event to worker for async processing
                await SendMergePdfsEvent(request);

                _logger.LogInformation("MergePdfsAsync event sent for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                return new MergePdfsResponse
                {
                    IsSuccess = true,
                    OutputPdfFileId = request.OutputPdfFileId,
                    Message = "Merge PDFs request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MergePdfsAsync for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));
                return new MergePdfsResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreatePdfsFromHtmlResponse> CreatePdfsFromHtmlAsync(CreatePdfsFromHtmlRequest request)
        {
            try
            {
                _logger.LogInformation("CreatePdfsFromHtmlAsync started for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                // Send event to worker
                await SendCreatePdfsFromHtmlEvent(request);

                _logger.LogInformation("CreatePdfsFromHtmlAsync event sent for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                return new CreatePdfsFromHtmlResponse
                {
                    IsSuccess = true,
                    MessageCoRelationId = request.MessageCoRelationId,
                    Message = "Create PDFs from HTML request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreatePdfsFromHtmlAsync for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));
                return new CreatePdfsFromHtmlResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<ExtractTextFromPdfsResponse> ExtractTextFromPdfsAsync(ExtractTextFromPdfsRequest request)
        {
            try
            {
                _logger.LogInformation("ExtractTextFromPdfsAsync started for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                // Send event to worker
                await SendExtractTextFromPdfsEvent(request);

                _logger.LogInformation("ExtractTextFromPdfsAsync event sent for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                return new ExtractTextFromPdfsResponse
                {
                    IsSuccess = true,
                    MessageCoRelationId = request.MessageCoRelationId,
                    Message = "Extract text from PDFs request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExtractTextFromPdfsAsync for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));
                return new ExtractTextFromPdfsResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreatePdfsFromHtmlUsingTEResponse> CreatePdfsFromHtmlUsingTEAsync(CreatePdfsFromHtmlUsingTERequest request)
        {
            try
            {
                _logger.LogInformation("CreatePdfsFromHtmlUsingTEAsync started for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                // Send event to worker
                await SendCreatePdfsFromHtmlUsingTEEvent(request);

                _logger.LogInformation("CreatePdfsFromHtmlUsingTEAsync event sent for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                return new CreatePdfsFromHtmlUsingTEResponse
                {
                    IsSuccess = true,
                    MessageCoRelationId = request.MessageCoRelationId,
                    Message = "Create PDFs from HTML using Template Engine request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreatePdfsFromHtmlUsingTEAsync for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));
                return new CreatePdfsFromHtmlUsingTEResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreatePdfsFromHtmlUsingTEBulkResponse> CreatePdfsFromHtmlUsingTEBulkAsync(CreatePdfsFromHtmlUsingTEBulkRequest request)
        {
            try
            {
                _logger.LogInformation("CreatePdfsFromHtmlUsingTEBulkAsync started for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                // Send event to worker
                await SendCreatePdfsFromHtmlUsingTEBulkEvent(request);

                _logger.LogInformation("CreatePdfsFromHtmlUsingTEBulkAsync event sent for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));

                return new CreatePdfsFromHtmlUsingTEBulkResponse
                {
                    IsSuccess = true,
                    MessageCoRelationId = request.MessageCoRelationId,
                    Message = "Bulk create PDFs from HTML using Template Engine request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreatePdfsFromHtmlUsingTEBulkAsync for MessageCoRelationId: {MessageCoRelationId}", LogSanitizer.Scrub(request.MessageCoRelationId));
                return new CreatePdfsFromHtmlUsingTEBulkResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<FixPdfsResponse> FixPdfsAsync(FixPdfsRequest request)
        {
            try
            {
                _logger.LogInformation("FixPdfsAsync started for MessageCorrelationId: {MessageCorrelationId}", LogSanitizer.Scrub(request.MessageCorrelationId));

                if (request.PdfInfos == null || !request.PdfInfos.Any())
                {
                    return new FixPdfsResponse
                    {
                        IsSuccess = false,
                        Message = "PdfInfos cannot be null or empty"
                    };
                }

                // Send event to worker
                await SendFixPdfsEvent(request);

                _logger.LogInformation("FixPdfsAsync event sent for MessageCorrelationId: {MessageCorrelationId}", LogSanitizer.Scrub(request.MessageCorrelationId));

                return new FixPdfsResponse
                {
                    IsSuccess = true,
                    MessageCorrelationId = request.MessageCorrelationId,
                    Message = "Fix PDFs request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FixPdfsAsync for MessageCorrelationId: {MessageCorrelationId}", LogSanitizer.Scrub(request.MessageCorrelationId));
                return new FixPdfsResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<StampImageToPdfResponse> StampImageToPdfAsync(StampImageToPdfRequest request)
        {
            try
            {
                _logger.LogInformation("StampImageToPdfAsync started for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                // Send event to worker
                await SendStampImageToPdfEvent(request);

                _logger.LogInformation("StampImageToPdfAsync event sent for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                return new StampImageToPdfResponse
                {
                    IsSuccess = true,
                    OutputPdfFileId = request.OutputPdfFileId,
                    Message = "Stamp image to PDF request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StampImageToPdfAsync for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));
                return new StampImageToPdfResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<StampTextToPdfResponse> StampTextToPdfAsync(StampTextToPdfRequest request)
        {
            try
            {
                _logger.LogInformation("StampTextToPdfAsync started for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                // Send event to worker
                await SendStampTextToPdfEvent(request);

                _logger.LogInformation("StampTextToPdfAsync event sent for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                return new StampTextToPdfResponse
                {
                    IsSuccess = true,
                    OutputPdfFileId = request.OutputPdfFileId,
                    Message = "Stamp text to PDF request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StampTextToPdfAsync for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));
                return new StampTextToPdfResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<StampIntoPdfResponse> StampIntoPdfAsync(StampIntoPdfRequest request)
        {
            try
            {
                _logger.LogInformation("StampIntoPdfAsync started for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                // Send event to worker
                await SendStampIntoPdfEvent(request);

                _logger.LogInformation("StampIntoPdfAsync event sent for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));

                return new StampIntoPdfResponse
                {
                    IsSuccess = true,
                    OutputPdfFileId = request.OutputPdfFileId,
                    Message = "Stamp into PDF request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in StampIntoPdfAsync for OutputPdfFileId: {OutputPdfFileId}", LogSanitizer.Scrub(request.OutputPdfFileId));
                return new StampIntoPdfResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        #region Private Helper Methods - Send Events

        private async Task SendMergePdfsEvent(MergePdfsRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<MergePdfsEvent>
                {
                    ConsumerName = PdfGeneratorConstants.MergePdfsQueue,
                    Payload = new MergePdfsEvent
                    {
                        OutputPdfFileId = request.OutputPdfFileId,
                        OutputPdfFileName = request.OutputPdfFileName,
                        MessageCoRelationId = request.MessageCoRelationId,
                        Engine = (int)request.Engine,
                        PdfFilesToBeMerged = request.PdfFilesToBeMerged,
                        EventReferenceData = request.EventReferenceData,
                        OpenInBrowser = request.OpenInBrowser,
                        HandleCorruptedPdf = request.HandleCorruptedPdf,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendCreatePdfsFromHtmlEvent(CreatePdfsFromHtmlRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreatePdfsFromHtmlEvent>
                {
                    ConsumerName = PdfGeneratorConstants.CreatePdfsFromHtmlQueue,
                    Payload = new CreatePdfsFromHtmlEvent
                    {
                        MessageCoRelationId = request.MessageCoRelationId,
                        EventReferenceData = request.EventReferenceData,
                        CreateFromHtmlCommands = request.CreateFromHtmlCommands,
                        Engine = (int)request.Engine,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendExtractTextFromPdfsEvent(ExtractTextFromPdfsRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<ExtractTextFromPdfsEvent>
                {
                    ConsumerName = PdfGeneratorConstants.ExtractTextFromPdfsQueue,
                    Payload = new ExtractTextFromPdfsEvent
                    {
                        MessageCoRelationId = request.MessageCoRelationId,
                        EventReferenceData = request.EventReferenceData,
                        Engine = request.Engine,
                        ExtractTextCommands = request.ExtractTextCommands,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendCreatePdfsFromHtmlUsingTEEvent(CreatePdfsFromHtmlUsingTERequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreatePdfsFromHtmlUsingTEEvent>
                {
                    ConsumerName = PdfGeneratorConstants.CreatePdfsUsingTEQueue,
                    Payload = new CreatePdfsFromHtmlUsingTEEvent
                    {
                        MessageCoRelationId = request.MessageCoRelationId,
                        EventReferenceData = request.EventReferenceData,
                        CreateFromHtmlCommands = request.CreateFromHtmlCommands,
                        Engine = (int)request.Engine,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendCreatePdfsFromHtmlUsingTEBulkEvent(CreatePdfsFromHtmlUsingTEBulkRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreatePdfsFromHtmlUsingTEBulkEvent>
                {
                    ConsumerName = PdfGeneratorConstants.CreatePdfsUsingTEBulkQueue,
                    Payload = new CreatePdfsFromHtmlUsingTEBulkEvent
                    {
                        MessageCoRelationId = request.MessageCoRelationId,
                        EventReferenceData = request.EventReferenceData,
                        CreateFromHtmlCommands = request.CreateFromHtmlCommands,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        Engine = (int)request.Engine,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendFixPdfsEvent(FixPdfsRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<FixPdfsEvent>
                {
                    ConsumerName = PdfGeneratorConstants.FixPdfsQueue,
                    Payload = new FixPdfsEvent
                    {
                        MessageCorrelationId = request.MessageCorrelationId,
                        PdfInfos = request.PdfInfos,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendStampImageToPdfEvent(StampImageToPdfRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<StampImageToPdfEvent>
                {
                    ConsumerName = PdfGeneratorConstants.StampImageToPdfQueue,
                    Payload = new StampImageToPdfEvent
                    {
                        PdfFileId = request.PdfFileId,
                        OutputPdfFileId = request.OutputPdfFileId,
                        OutputPdfFileName = request.OutputPdfFileName,
                        MessageCoRelationId = request.MessageCoRelationId,
                        Stamps = request.Stamps,
                        Engine = (int)request.Engine,
                        EventReferenceData = request.EventReferenceData,
                        OpenInBrowser = request.OpenInBrowser,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendStampTextToPdfEvent(StampTextToPdfRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<StampTextToPdfEvent>
                {
                    ConsumerName = PdfGeneratorConstants.StampTextToPdfQueue,
                    Payload = new StampTextToPdfEvent
                    {
                        PdfFileId = request.PdfFileId,
                        OutputPdfFileId = request.OutputPdfFileId,
                        OutputPdfFileName = request.OutputPdfFileName,
                        MessageCoRelationId = request.MessageCoRelationId,
                        Stamps = request.Stamps,
                        Engine = (int)request.Engine,
                        EventReferenceData = request.EventReferenceData,
                        OpenInBrowser = request.OpenInBrowser,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        private async Task SendStampIntoPdfEvent(StampIntoPdfRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<StampIntoPdfEvent>
                {
                    ConsumerName = PdfGeneratorConstants.StampIntoPdfQueue,
                    Payload = new StampIntoPdfEvent
                    {
                        PdfFileId = request.PdfFileId,
                        OutputPdfFileId = request.OutputPdfFileId,
                        OutputPdfFileName = request.OutputPdfFileName,
                        MessageCoRelationId = request.MessageCoRelationId,
                        Stamps = request.Stamps,
                        Engine = (int)request.Engine,
                        EventReferenceData = request.EventReferenceData,
                        OpenInBrowser = request.OpenInBrowser,
                        ProjectKey = request.ProjectKey
                    }
                }
            );
        }

        #endregion
    }
}

