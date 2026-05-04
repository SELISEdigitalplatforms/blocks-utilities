using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class RenderWithJsonConsumer : IConsumer<RenderWithJsonEvent>
    {
        private readonly ILogger<RenderWithJsonConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly ITemplateEngineNotificationService _notificationService;

        public RenderWithJsonConsumer(
            ILogger<RenderWithJsonConsumer> logger,
            StorageHelper storageHelper,
            TemplateRenderingService templateRenderingService,
            ITemplateEngineNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _templateRenderingService = templateRenderingService;
            _notificationService = notificationService;
        }

        public async Task Consume(RenderWithJsonEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("RenderWithJsonConsumer: Processing RenderWithJsonEvent for RenderedFileId={RenderedFileId}, TenantId={TenantId}", @event.RenderedFileId, tenantId);

            try
            {
                // Step 1: Get template file from storage
                _logger.LogInformation("RenderWithJsonConsumer: Fetching template file with TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                var templateContent = await _storageHelper.GetFileContentAsString(@event.TemplateFileId, @event.ProjectKey ?? tenantId);
                
                if (string.IsNullOrEmpty(templateContent))
                {
                    _logger.LogError("RenderWithJsonConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyRenderWithJsonEvent(false, @event.RenderedFileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("RenderWithJsonConsumer: Template content fetched successfully, length={Length}", templateContent.Length);

                // Step 2: Render template with JSON data
                _logger.LogInformation("RenderWithJsonConsumer: Rendering template with JSON data");
                var renderedContent = _templateRenderingService.RenderTemplateWithJson(templateContent, @event.JSONString);

                if (string.IsNullOrEmpty(renderedContent))
                {
                    _logger.LogError("RenderWithJsonConsumer: Rendered content is null or empty for RenderedFileId={RenderedFileId}", @event.RenderedFileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyRenderWithJsonEvent(false, @event.RenderedFileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("RenderWithJsonConsumer: Template rendered successfully, length={Length}", renderedContent.Length);

                // Step 3: Convert to stream
                byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                MemoryStream resultStream = new MemoryStream(resultByteArray);

                // Step 4: Save to storage
                var fileNameExtension = @event.FileNameExtension.Trim();
                var fileName = @event.RenderedFileId + fileNameExtension;

                var metadata = new Dictionary<string, string>
                {
                    { "RenderedFileId", @event.RenderedFileId },
                    { "TemplateFileId", @event.TemplateFileId },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "RenderedTemplate" }
                };

                _logger.LogInformation("RenderWithJsonConsumer: Saving rendered file to storage with fileName={FileName}", fileName);
                var saveSuccess = await _storageHelper.SaveFileToStorage(
                    resultStream,
                    @event.RenderedFileId,
                    fileName,
                    metadata,
                    "Blocks-Template-Rendered-Files");

                if (!saveSuccess)
                {
                    _logger.LogError("RenderWithJsonConsumer: Failed to save file to storage for RenderedFileId={RenderedFileId}", @event.RenderedFileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyRenderWithJsonEvent(false, @event.RenderedFileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("RenderWithJsonConsumer: File saved successfully to storage for RenderedFileId={RenderedFileId}", @event.RenderedFileId);

                // Step 5: Send success notification
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyRenderWithJsonEvent(true, @event.RenderedFileId, @event.SubscriptionFilterId, @event.ProjectKey);
                }

                _logger.LogInformation("RenderWithJsonConsumer: Successfully processed RenderWithJsonEvent for RenderedFileId={RenderedFileId}", @event.RenderedFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RenderWithJsonConsumer: Exception occurred while processing RenderWithJsonEvent for RenderedFileId={RenderedFileId}", @event.RenderedFileId);
                
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyRenderWithJsonEvent(false, @event.RenderedFileId, @event.SubscriptionFilterId, @event.ProjectKey);
                }
            }
        }
    }
}
