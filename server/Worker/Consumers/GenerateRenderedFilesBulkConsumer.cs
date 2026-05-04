using Blocks.Genesis;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class GenerateRenderedFilesBulkConsumer : IConsumer<GenerateRenderedFilesBulkEvent>
    {
        private readonly ILogger<GenerateRenderedFilesBulkConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly ITemplateEngineRepository _templateEngineRepository;
        private readonly ITemplateEngineNotificationService _notificationService;

        public GenerateRenderedFilesBulkConsumer(
            ILogger<GenerateRenderedFilesBulkConsumer> logger,
            StorageHelper storageHelper,
            TemplateRenderingService templateRenderingService,
            ITemplateEngineRepository templateEngineRepository,
            ITemplateEngineNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _templateRenderingService = templateRenderingService;
            _templateEngineRepository = templateEngineRepository;
            _notificationService = notificationService;
        }

        public async Task Consume(GenerateRenderedFilesBulkEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("GenerateRenderedFilesBulkConsumer: Processing bulk event with {RequestCount} requests, TenantId={TenantId}", @event.GenerateRenderedFileRequests?.Count ?? 0, tenantId);

            var successCount = 0;
            var failureCount = 0;

            try
            {
                foreach (var request in @event.GenerateRenderedFileRequests ?? new List<GenerateRenderedFileRequest>())
                {
                    try
                    {
                        _logger.LogInformation("GenerateRenderedFilesBulkConsumer: Processing request FileId={FileId}", request.FileId);

                        // Step 1: Get template file from storage
                        var templateContent = await _storageHelper.GetFileContentAsString(
                            request.TemplateFileId, 
                            @event.ProjectKey ?? tenantId);
                        
                        if (string.IsNullOrEmpty(templateContent))
                        {
                            _logger.LogError("GenerateRenderedFilesBulkConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", request.TemplateFileId);
                            failureCount++;
                            continue;
                        }

                        // Step 2: Fetch entity data from MongoDB
                        var entities = new Dictionary<string, object>();
                        
                        foreach (var entityIdentifier in request.EntityIdentifierList ?? new List<EntityParams>())
                        {
                            var entityDataJson = await _templateEngineRepository.GetEntityByItemIdAsync(
                                entityIdentifier.EntityName, 
                                entityIdentifier.EntityItemId);

                            if (!string.IsNullOrEmpty(entityDataJson))
                            {
                                var entityData = JsonConvert.DeserializeObject<Dictionary<string, object>>(entityDataJson);
                                if (entityData != null)
                                {
                                    entities[entityIdentifier.EntityName] = entityData;
                                }
                            }
                        }

                        // Step 3: Prepare metadata
                        var metadata = new Dictionary<string, object>();
                        foreach (var metaDataItem in request.MetaDataList ?? new List<MetaData>())
                        {
                            if (!string.IsNullOrEmpty(metaDataItem.Value))
                            {
                                metadata[metaDataItem.Name] = metaDataItem.Value;
                            }
                            else if (metaDataItem.Values != null)
                            {
                                metadata[metaDataItem.Name] = metaDataItem.Values;
                            }
                        }

                        // Step 4: Render template
                        var renderedContent = _templateRenderingService.RenderTemplateWithEntityData(
                            templateContent, 
                            entities, 
                            metadata);

                        if (string.IsNullOrEmpty(renderedContent))
                        {
                            _logger.LogError("GenerateRenderedFilesBulkConsumer: Rendered content is null or empty for FileId={FileId}", request.FileId);
                            failureCount++;
                            continue;
                        }

                        // Step 5: Save to storage
                        byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                        MemoryStream resultStream = new MemoryStream(resultByteArray);

                        var fileNameExtension = request.FileNameExtension.Trim();
                        var fileName = request.FileId + fileNameExtension;

                        var storageMetadata = new Dictionary<string, string>
                        {
                            { "FileId", request.FileId },
                            { "TemplateFileId", request.TemplateFileId },
                            { "BulkSubscriptionFilterId", @event.BulkSubscriptionFilterId ?? "" },
                            { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "EntityCount", entities.Count.ToString() },
                            { "FileType", "GeneratedFromEntityBulk" }
                        };

                        var saveSuccess = await _storageHelper.SaveFileToStorage(
                            resultStream,
                            request.FileId,
                            fileName,
                            storageMetadata,
                            "Blocks-Template-Generated-Files");

                        if (saveSuccess)
                        {
                            successCount++;
                            _logger.LogInformation("GenerateRenderedFilesBulkConsumer: Successfully processed FileId={FileId}", request.FileId);
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogError("GenerateRenderedFilesBulkConsumer: Failed to save file for FileId={FileId}", request.FileId);
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.LogError(ex, "GenerateRenderedFilesBulkConsumer: Exception processing request FileId={FileId}", request.FileId);
                    }
                }

                _logger.LogInformation("GenerateRenderedFilesBulkConsumer: Bulk processing completed. Success={SuccessCount}, Failure={FailureCount}", successCount, failureCount);

                // Send notification
                await _notificationService.NotifyGenerateRenderedFilesBulkEvent(
                    failureCount == 0, 
                    @event.BulkSubscriptionFilterId, 
                    @event.ProjectKey,
                    successCount,
                    failureCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateRenderedFilesBulkConsumer: Exception occurred during bulk processing");
                
                await _notificationService.NotifyGenerateRenderedFilesBulkEvent(
                    false, 
                    @event.BulkSubscriptionFilterId, 
                    @event.ProjectKey,
                    0,
                    @event.GenerateRenderedFileRequests.Count);
            }
        }
    }
}
