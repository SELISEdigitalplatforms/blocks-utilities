using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.Events;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    [ExcludeFromCodeCoverage]
    public class CreatePdfsFromHtmlConsumer : IConsumer<CreatePdfsFromHtmlEvent>
    {
        private readonly ILogger<CreatePdfsFromHtmlConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfEngineProvider _engineProvider;
        private readonly IPdfGeneratorRepository _repository;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public CreatePdfsFromHtmlConsumer(
            ILogger<CreatePdfsFromHtmlConsumer> logger,
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

        public async Task Consume(CreatePdfsFromHtmlEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("CreatePdfsFromHtmlConsumer: Processing event for MessageCoRelationId={MessageCoRelationId}, TenantId={TenantId}", @event.MessageCoRelationId, tenantId);

            int successCount = 0;
            int failureCount = 0;

            try
            {
                var engine = _engineProvider.GetEngine(@event.Engine);

                foreach (var createCommand in @event.CreateFromHtmlCommands)
                {
                    try
                    {
                        _logger.LogInformation("CreatePdfsFromHtmlConsumer: Processing OutputPdfFileId={OutputPdfFileId}", createCommand.OutputPdfFileId);

                        // Get HTML content
                        var htmlContent = await _storageHelper.GetHtmlContentAsString(createCommand.HtmlFileId, @event.ProjectKey);
                        if (string.IsNullOrEmpty(htmlContent))
                        {
                            _logger.LogError("CreatePdfsFromHtmlConsumer: Failed to get HTML content for HtmlFileId={HtmlFileId}", createCommand.HtmlFileId);
                            failureCount++;
                            continue;
                        }

                        // Build PDF generation options
                        var options = await BuildPdfGenerationOptions(createCommand, @event.ProjectKey, tenantId);

                        // Convert HTML to PDF
                        _logger.LogInformation("CreatePdfsFromHtmlConsumer: Converting HTML to PDF");
                        var pdfStream = await engine.ConvertHtmlToPdfAsync(htmlContent, options);

                        if (pdfStream == null || pdfStream.Length == 0)
                        {
                            _logger.LogError("CreatePdfsFromHtmlConsumer: Failed to convert HTML to PDF for OutputPdfFileId={OutputPdfFileId}", createCommand.OutputPdfFileId);
                            failureCount++;
                            continue;
                        }

                        _logger.LogInformation("CreatePdfsFromHtmlConsumer: Successfully generated PDF, size={PdfSize} bytes", pdfStream.Length);

                        // Save to storage
                        var metadata = new Dictionary<string, string>
                        {
                            { "MessageCoRelationId", @event.MessageCoRelationId },
                            { "SourceHtmlFileId", createCommand.HtmlFileId },
                            { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "FileType", "GeneratedPDF" },
                            { "Engine", @event.Engine.ToString() },
                            { "OpenInBrowser", createCommand.OpenInBrowser.ToString() }
                        };

                        var saveSuccess = await _storageHelper.SavePdfToStorage(
                            pdfStream,
                            createCommand.OutputPdfFileId,
                            createCommand.OutputPdfFileName,
                            metadata,
                            "Blocks-PDF-Generated-Files",
                            @event.ProjectKey);

                        if (saveSuccess)
                        {
                            _logger.LogInformation("CreatePdfsFromHtmlConsumer: Successfully saved PDF for OutputPdfFileId={OutputPdfFileId}", createCommand.OutputPdfFileId);
                            successCount++;
                        }
                        else
                        {
                            _logger.LogError("CreatePdfsFromHtmlConsumer: Failed to save PDF for OutputPdfFileId={OutputPdfFileId}", createCommand.OutputPdfFileId);
                            failureCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "CreatePdfsFromHtmlConsumer: Error processing OutputPdfFileId={OutputPdfFileId}", createCommand.OutputPdfFileId);
                        failureCount++;
                    }
                }

                // Send notification
                await _notificationService.NotifyCreatePdfsFromHtmlEvent(
                    failureCount == 0,
                    @event.MessageCoRelationId,
                    @event.ProjectKey,
                    successCount,
                    failureCount);

                _logger.LogInformation("CreatePdfsFromHtmlConsumer: Completed processing. Success={SuccessCount}, Failures={FailureCount}", successCount, failureCount);
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "CreatePdfsFromHtmlConsumer: Exception occurred for MessageCoRelationId={MessageCoRelationId}", @event.MessageCoRelationId);
                await _notificationService.NotifyCreatePdfsFromHtmlEvent(false, @event.MessageCoRelationId, @event.ProjectKey, successCount, failureCount);
            }
        }

        private async Task<PdfGenerationOptions> BuildPdfGenerationOptions(CreateFromHtmlCommand command, string? projectKey, string tenantId)
        {
            var options = new PdfGenerationOptions
            {
                HeaderHeight = command.HeaderHeight,
                FooterHeight = command.FooterHeight,
                IsPageNumberEnabled = command.IsPageNumberEnabled,
                IsTotalPageCountEnabled = command.IsTotalPageCountEnabled,
                UseFormatting = command.UseFormatting,
                OpenInBrowser = command.OpenInBrowser,
                ProfileId = command.Profile,
                PageNumberText = command.PageNumberText
            };

            // Get header/footer HTML if provided
            if (command.HasHeader && !string.IsNullOrEmpty(command.HeaderHtmlFileId))
            {
                options.HeaderHtml = await _storageHelper.GetHtmlContentAsString(command.HeaderHtmlFileId, projectKey);
            }

            if (command.HasFooter && !string.IsNullOrEmpty(command.FooterHtmlFileId))
            {
                options.FooterHtml = await _storageHelper.GetHtmlContentAsString(command.FooterHtmlFileId, projectKey);
            }

            if (command.HasFirstPageHeader && !string.IsNullOrEmpty(command.FirstPageHeaderFileId))
            {
                options.FirstPageHeaderHtml = await _storageHelper.GetHtmlContentAsString(command.FirstPageHeaderFileId, projectKey);
            }

            if (command.HasFirstPageFooter && !string.IsNullOrEmpty(command.FirstPageFooterFileId))
            {
                options.FirstPageFooterHtml = await _storageHelper.GetHtmlContentAsString(command.FirstPageFooterFileId, projectKey);
            }

            // Get PDF utility profile if specified
            if (!string.IsNullOrEmpty(command.Profile))
            {
                var profile = await _repository.GetPdfUtilityProfileAsync(command.Profile, tenantId);
                if (profile != null)
                {
                    options.Profile = profile;
                    _logger.LogInformation("CreatePdfsFromHtmlConsumer: Loaded PDF utility profile ID={ProfileId}", command.Profile);
                }
                else
                {
                    _logger.LogWarning("CreatePdfsFromHtmlConsumer: PDF utility profile not found for ID={ProfileId}", command.Profile);
                }
            }

            return options;
        }
    }
}
