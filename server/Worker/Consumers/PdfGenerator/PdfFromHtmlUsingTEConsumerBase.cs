using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Base class for PDF generation from HTML using Template Engine consumers
    /// Eliminates duplication between regular and bulk consumers
    /// </summary>
    /// <typeparam name="TEvent">The event type (CreatePdfsFromHtmlUsingTEEvent or Bulk variant)</typeparam>
    /// <typeparam name="TCommand">The command type (CreateFromHtmlUsingTECommand or Bulk variant)</typeparam>
    [ExcludeFromCodeCoverage]
    public abstract class PdfFromHtmlUsingTEConsumerBase<TEvent, TCommand>
        where TEvent : class
        where TCommand : class
    {
        protected readonly ILogger _logger;
        protected readonly PdfStorageHelper _storageHelper;
        protected readonly IPdfEngineProvider _engineProvider;
        protected readonly IPdfGeneratorRepository _repository;
        protected readonly IPdfGeneratorNotificationService _notificationService;
        protected readonly ITemplateEngineService _templateEngineService;

        protected PdfFromHtmlUsingTEConsumerBase(
            ILogger logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorRepository repository,
            IPdfGeneratorNotificationService notificationService,
            ITemplateEngineService templateEngineService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _engineProvider = engineProvider;
            _repository = repository;
            _notificationService = notificationService;
            _templateEngineService = templateEngineService;
        }

        /// <summary>
        /// Main processing logic - shared by both regular and bulk consumers
        /// </summary>
        protected async Task ConsumeInternal(
            string messageCoRelationId,
            string? projectKey,
            int engine,
            IEnumerable<TCommand> commands)
        {
            var tenantId = projectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("{ConsumerName}: Processing event for MessageCoRelationId={MessageCoRelationId}, TenantId={TenantId}", GetConsumerName(), messageCoRelationId, tenantId);

            int successCount = 0;
            int failureCount = 0;

            try
            {
                var pdfEngine = _engineProvider.GetEngine(engine);

                foreach (var createCommand in commands)
                {
                    try
                    {
                        var outputFileId = GetOutputFileId(createCommand);
                        var templateFileId = GetTemplateFileId(createCommand);

                        _logger.LogInformation("{ConsumerName}: Processing OutputPdfFileId={OutputPdfFileId}, TemplateFileId={TemplateFileId}", GetConsumerName(), outputFileId, templateFileId);

                        // Generate HTML from template engine
                        var htmlContent = await GenerateHtmlFromTemplate(createCommand, projectKey, tenantId);
                        if (string.IsNullOrEmpty(htmlContent))
                        {
                        _logger.LogError("{ConsumerName}: Failed to generate HTML from template for TemplateFileId={TemplateFileId}", GetConsumerName(), templateFileId);
                            failureCount++;
                            continue;
                        }

                        // Build PDF generation options
                        var options = await BuildPdfGenerationOptions(createCommand, projectKey, tenantId);

                        // Convert HTML to PDF
                        _logger.LogInformation("{ConsumerName}: Converting HTML to PDF", GetConsumerName());
                        var pdfStream = await pdfEngine.ConvertHtmlToPdfAsync(htmlContent, options);

                        if (pdfStream == null || pdfStream.Length == 0)
                        {
                        _logger.LogError("{ConsumerName}: Failed to convert HTML to PDF for OutputPdfFileId={OutputPdfFileId}", GetConsumerName(), outputFileId);
                            failureCount++;
                            continue;
                        }

                        _logger.LogInformation("{ConsumerName}: Successfully generated PDF, size={PdfSize} bytes", GetConsumerName(), pdfStream.Length);

                        // Save to storage
                        var metadata = BuildMetadata(messageCoRelationId, templateFileId, engine, createCommand);
                        var saveSuccess = await _storageHelper.SavePdfToStorage(
                            pdfStream,
                            outputFileId,
                            GetOutputFileName(createCommand),
                            metadata,
                            GetStorageContainer(),
                            projectKey);

                        if (saveSuccess)
                        {
                            _logger.LogInformation("{ConsumerName}: Successfully saved PDF for OutputPdfFileId={OutputPdfFileId}", GetConsumerName(), outputFileId);
                            successCount++;
                        }
                        else
                        {
                            _logger.LogError("{ConsumerName}: Failed to save PDF for OutputPdfFileId={OutputPdfFileId}", GetConsumerName(), outputFileId);
                            failureCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{ConsumerName}: Error processing OutputPdfFileId={OutputPdfFileId}", GetConsumerName(), GetOutputFileId(createCommand));
                        failureCount++;
                    }
                }

                // Send notification
                await SendNotificationAsync(messageCoRelationId, projectKey, successCount, failureCount);

                _logger.LogInformation("{ConsumerName}: Completed processing. Success={SuccessCount}, Failures={FailureCount}", GetConsumerName(), successCount, failureCount);
            }
            catch (Exception ex)
            {
            _logger.LogError(ex, "{ConsumerName}: Exception occurred for MessageCoRelationId={MessageCoRelationId}", GetConsumerName(), messageCoRelationId);
                await SendNotificationAsync(messageCoRelationId, projectKey, successCount, failureCount, isError: true);
            }
        }

        /// <summary>
        /// Generates HTML content from template using Template Engine
        /// </summary>
        protected async Task<string?> GenerateHtmlFromTemplate(TCommand command, string? projectKey, string tenantId)
        {
            try
            {
                var templateFileId = GetTemplateFileId(command);
                var metaDataList = GetMetaDataList(command);
                var fileExtension = GetFileNameExtension(command);

                // Build JSON string from MetaDataList
                var jsonData = new Dictionary<string, object>();
                if (metaDataList != null && metaDataList.Any())
                {
                    foreach (var meta in metaDataList)
                    {
                        jsonData[meta.Key] = meta.Value ?? string.Empty;
                    }
                }

                var jsonString = JsonSerializer.Serialize(jsonData);
                var renderedFileId = $"rendered_{Guid.NewGuid()}{fileExtension}";

                // Call template engine service to render HTML
                var request = new RenderWithJsonRequest
                {
                    ProjectKey = projectKey,
                    TemplateFileId = templateFileId,
                    RenderedFileId = renderedFileId,
                    FileNameExtension = fileExtension,
                    JSONString = jsonString
                };

                var response = await _templateEngineService.RenderWithJsonAsync(request);
                
                if (response.IsSuccess)
                {
                    // Get the rendered HTML content from storage
                    return await _storageHelper.GetHtmlContentAsString(response.RenderedFileId, projectKey);
                }
                else
                {
                    _logger.LogError("{ConsumerName}: Template rendering failed for TemplateFileId={TemplateFileId}: {Message}", GetConsumerName(), templateFileId, response.Message);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ConsumerName}: Failed to generate HTML for TemplateFileId={TemplateFileId}", GetConsumerName(), GetTemplateFileId(command));
                return null;
            }
        }

        /// <summary>
        /// Builds PDF generation options from command properties
        /// </summary>
        protected async Task<PdfGenerationOptions> BuildPdfGenerationOptions(TCommand command, string? projectKey, string tenantId)
        {
            var options = new PdfGenerationOptions
            {
                HeaderHeight = GetHeaderHeight(command),
                FooterHeight = GetFooterHeight(command),
                IsPageNumberEnabled = GetIsPageNumberEnabled(command),
                IsTotalPageCountEnabled = GetIsTotalPageCountEnabled(command),
                UseFormatting = GetUseFormatting(command),
                OpenInBrowser = GetOpenInBrowser(command),
                ProfileId = GetProfile(command),
                PageNumberText = GetPageNumberText(command)
            };

            // Get header/footer HTML if provided
            if (GetHasHeader(command) && !string.IsNullOrEmpty(GetHeaderHtmlFileId(command)))
            {
                options.HeaderHtml = await _storageHelper.GetHtmlContentAsString(GetHeaderHtmlFileId(command), projectKey);
            }

            if (GetHasFooter(command) && !string.IsNullOrEmpty(GetFooterHtmlFileId(command)))
            {
                options.FooterHtml = await _storageHelper.GetHtmlContentAsString(GetFooterHtmlFileId(command), projectKey);
            }

            if (GetHasFirstPageHeader(command) && !string.IsNullOrEmpty(GetFirstPageHeaderFileId(command)))
            {
                options.FirstPageHeaderHtml = await _storageHelper.GetHtmlContentAsString(GetFirstPageHeaderFileId(command), projectKey);
            }

            if (GetHasFirstPageFooter(command) && !string.IsNullOrEmpty(GetFirstPageFooterFileId(command)))
            {
                options.FirstPageFooterHtml = await _storageHelper.GetHtmlContentAsString(GetFirstPageFooterFileId(command), projectKey);
            }

            // Get PDF utility profile if specified
            var profile = GetProfile(command);
            if (!string.IsNullOrEmpty(profile))
            {
                var profileData = await _repository.GetPdfUtilityProfileAsync(profile, tenantId);
                if (profileData != null)
                {
                    options.Profile = profileData;
                    _logger.LogInformation("{ConsumerName}: Loaded PDF utility profile ID={ProfileId}", GetConsumerName(), profile);
                }
                else
                {
                    _logger.LogWarning("{ConsumerName}: PDF utility profile not found for ID={ProfileId}", GetConsumerName(), profile);
                }
            }

            return options;
        }

        /// <summary>
        /// Builds metadata dictionary for storage
        /// </summary>
        protected Dictionary<string, string> BuildMetadata(
            string messageCoRelationId,
            string templateFileId,
            int engine,
            TCommand command)
        {
            return new Dictionary<string, string>
            {
                { "MessageCoRelationId", messageCoRelationId },
                { "TemplateFileId", templateFileId },
                { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                { "FileType", GetFileType() },
                { "Engine", engine.ToString() },
                { "OpenInBrowser", GetOpenInBrowser(command).ToString() }
            };
        }

        // Abstract methods - must be implemented by derived classes
        protected abstract string GetConsumerName();
        protected abstract string GetFileType();
        protected abstract string GetStorageContainer();
        protected abstract Task SendNotificationAsync(string messageCoRelationId, string? projectKey, int successCount, int failureCount, bool isError = false);

        // Abstract property accessors for TCommand
        protected abstract string GetOutputFileId(TCommand command);
        protected abstract string GetOutputFileName(TCommand command);
        protected abstract string GetTemplateFileId(TCommand command);
        protected abstract List<PdfMetaData>? GetMetaDataList(TCommand command);
        protected abstract string GetFileNameExtension(TCommand command);
        protected abstract double GetHeaderHeight(TCommand command);
        protected abstract double GetFooterHeight(TCommand command);
        protected abstract bool GetIsPageNumberEnabled(TCommand command);
        protected abstract bool GetIsTotalPageCountEnabled(TCommand command);
        protected abstract bool GetUseFormatting(TCommand command);
        protected abstract bool GetOpenInBrowser(TCommand command);
        protected abstract string? GetProfile(TCommand command);
        protected abstract string? GetPageNumberText(TCommand command);
        protected abstract bool GetHasHeader(TCommand command);
        protected abstract string? GetHeaderHtmlFileId(TCommand command);
        protected abstract bool GetHasFooter(TCommand command);
        protected abstract string? GetFooterHtmlFileId(TCommand command);
        protected abstract bool GetHasFirstPageHeader(TCommand command);
        protected abstract string? GetFirstPageHeaderFileId(TCommand command);
        protected abstract bool GetHasFirstPageFooter(TCommand command);
        protected abstract string? GetFirstPageFooterFileId(TCommand command);
    }
}
