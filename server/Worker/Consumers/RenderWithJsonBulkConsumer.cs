using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class RenderWithJsonBulkConsumer : IConsumer<RenderWithJsonBulkEvent>
    {
        private readonly ILogger<RenderWithJsonBulkConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly ITemplateEngineNotificationService _notificationService;

        public RenderWithJsonBulkConsumer(
            ILogger<RenderWithJsonBulkConsumer> logger,
            StorageHelper storageHelper,
            TemplateRenderingService templateRenderingService,
            ITemplateEngineNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _templateRenderingService = templateRenderingService;
            _notificationService = notificationService;
        }

        public async Task Consume(RenderWithJsonBulkEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("RenderWithJsonBulkConsumer: Processing bulk event with {PayloadCount} payloads, ReferenceId={ReferenceId}, TenantId={TenantId}", @event.Payloads?.Count ?? 0, @event.ReferenceId, tenantId);

            var successCount = 0;
            var failureCount = 0;

            try
            {
                foreach (var payload in @event.Payloads ?? new List<RenderWithJsonPayload>())
                {
                    try
                    {
                        _logger.LogInformation("RenderWithJsonBulkConsumer: Processing payload RenderedFileId={RenderedFileId}", payload.RenderedFileId);

                        // Step 1: Get template file from storage
                        var templateContent = await _storageHelper.GetFileContentAsString(
                            payload.TemplateFileId, 
                            @event.ProjectKey ?? tenantId);
                        
                        if (string.IsNullOrEmpty(templateContent))
                        {
                            _logger.LogError("RenderWithJsonBulkConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", payload.TemplateFileId);
                            failureCount++;
                            continue;
                        }

                        // Step 2: Render template with JSON data
                        var renderedContent = _templateRenderingService.RenderTemplateWithJson(templateContent, payload.JSONString);

                        if (string.IsNullOrEmpty(renderedContent))
                        {
                            _logger.LogError("RenderWithJsonBulkConsumer: Rendered content is null or empty for RenderedFileId={RenderedFileId}", payload.RenderedFileId);
                            failureCount++;
                            continue;
                        }

                        // Step 3: Save to storage
                        byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                        MemoryStream resultStream = new MemoryStream(resultByteArray);

                        var fileNameExtension = payload.FileNameExtension.Trim();
                        var fileName = payload.RenderedFileId + fileNameExtension;

                        var metadata = new Dictionary<string, string>
                        {
                            { "RenderedFileId", payload.RenderedFileId },
                            { "TemplateFileId", payload.TemplateFileId },
                            { "BulkReferenceId", @event.ReferenceId },
                            { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "FileType", "RenderedTemplateBulk" }
                        };

                        var saveSuccess = await _storageHelper.SaveFileToStorage(
                            resultStream,
                            payload.RenderedFileId,
                            fileName,
                            metadata,
                            "Blocks-Template-Rendered-Files");

                        if (saveSuccess)
                        {
                            successCount++;
                            _logger.LogInformation("RenderWithJsonBulkConsumer: Successfully processed RenderedFileId={RenderedFileId}", payload.RenderedFileId);
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogError("RenderWithJsonBulkConsumer: Failed to save file for RenderedFileId={RenderedFileId}", payload.RenderedFileId);
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.LogError(ex, "RenderWithJsonBulkConsumer: Exception processing payload RenderedFileId={RenderedFileId}", payload.RenderedFileId);
                    }
                }

                _logger.LogInformation("RenderWithJsonBulkConsumer: Bulk processing completed. Success={SuccessCount}, Failure={FailureCount}", successCount, failureCount);

                // Send notification
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyRenderWithJsonBulkEvent(
                        failureCount == 0, 
                        @event.ReferenceId, 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        successCount,
                        failureCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RenderWithJsonBulkConsumer: Exception occurred during bulk processing for ReferenceId={ReferenceId}", @event.ReferenceId);
                
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyRenderWithJsonBulkEvent(
                        false, 
                        @event.ReferenceId, 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        0,
                        @event.Payloads.Count);
                }
            }
        }
    }
}
