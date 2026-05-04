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
    public class GenerateRenderedFileConsumer : IConsumer<GenerateRenderedFileEvent>
    {
        private readonly ILogger<GenerateRenderedFileConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly ITemplateEngineRepository _templateEngineRepository;
        private readonly ITemplateEngineNotificationService _notificationService;

        public GenerateRenderedFileConsumer(
            ILogger<GenerateRenderedFileConsumer> logger,
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

        public async Task Consume(GenerateRenderedFileEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("GenerateRenderedFileConsumer: Processing GenerateRenderedFileEvent for FileId={FileId}, TenantId={TenantId}", @event.FileId, tenantId);

            try
            {
                // Step 1: Check if file already exists (optional - can skip for now)
                // TODO: Implement file existence check if needed

                // Step 2: Get template file from storage
                _logger.LogInformation("GenerateRenderedFileConsumer: Fetching template file TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                var templateContent = await _storageHelper.GetFileContentAsString(@event.TemplateFileId, @event.ProjectKey ?? tenantId);
                
                if (string.IsNullOrEmpty(templateContent))
                {
                _logger.LogError("GenerateRenderedFileConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                    await _notificationService.NotifyGenerateRenderedFileEvent(false, @event.FileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("GenerateRenderedFileConsumer: Template content fetched successfully, length={Length}", templateContent.Length);

                // Step 3: Fetch entity data from MongoDB
                var entities = new Dictionary<string, object>();
                
                _logger.LogInformation("GenerateRenderedFileConsumer: Fetching {EntityCount} entities from MongoDB", @event.EntityIdentifierList?.Count ?? 0);
                foreach (var entityIdentifier in @event.EntityIdentifierList ?? new List<EntityParams>())
                {
                    _logger.LogInformation("GenerateRenderedFileConsumer: Fetching entity '{EntityName}' with ItemId='{EntityItemId}'", entityIdentifier.EntityName, entityIdentifier.EntityItemId);
                    
                    var entityDataJson = await _templateEngineRepository.GetEntityByItemIdAsync(
                        entityIdentifier.EntityName, 
                        entityIdentifier.EntityItemId);

                    if (string.IsNullOrEmpty(entityDataJson))
                    {
                    _logger.LogWarning("GenerateRenderedFileConsumer: No data found for entity '{EntityName}' with ItemId='{EntityItemId}'", entityIdentifier.EntityName, entityIdentifier.EntityItemId);
                        continue;
                    }

                    // Deserialize entity data
                    var entityData = JsonConvert.DeserializeObject<Dictionary<string, object>>(entityDataJson);
                    if (entityData != null)
                    {
                        entities[entityIdentifier.EntityName] = entityData;
                        _logger.LogInformation("GenerateRenderedFileConsumer: Successfully fetched entity '{EntityName}'", entityIdentifier.EntityName);
                    }
                }

                // Step 4: Prepare metadata
                var metadata = new Dictionary<string, object>();
                
                _logger.LogInformation("GenerateRenderedFileConsumer: Processing {MetadataCount} metadata items", @event.MetaDataList?.Count ?? 0);
                foreach (var metaDataItem in @event.MetaDataList ?? new List<MetaData>())
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

                // Step 5: Render template with entity data and metadata
                _logger.LogInformation("GenerateRenderedFileConsumer: Rendering template with {EntityCount} entities and {MetadataCount} metadata items", entities.Count, metadata.Count);
                var renderedContent = _templateRenderingService.RenderTemplateWithEntityData(
                    templateContent, 
                    entities, 
                    metadata);

                if (string.IsNullOrEmpty(renderedContent))
                {
                _logger.LogError("GenerateRenderedFileConsumer: Rendered content is null or empty for FileId={FileId}", @event.FileId);
                    await _notificationService.NotifyGenerateRenderedFileEvent(false, @event.FileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("GenerateRenderedFileConsumer: Template rendered successfully, length={Length}", renderedContent.Length);

                // Step 6: Convert to stream and save to storage
                byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                MemoryStream resultStream = new MemoryStream(resultByteArray);

                var fileNameExtension = @event.FileNameExtension.Trim();
                var fileName = @event.FileId + fileNameExtension;

                var storageMetadata = new Dictionary<string, string>
                {
                    { "FileId", @event.FileId },
                    { "TemplateFileId", @event.TemplateFileId },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "EntityCount", entities.Count.ToString() },
                    { "FileType", "GeneratedFromEntity" }
                };

                _logger.LogInformation("GenerateRenderedFileConsumer: Saving rendered file to storage with fileName={FileName}", fileName);
                var saveSuccess = await _storageHelper.SaveFileToStorage(
                    resultStream,
                    @event.FileId,
                    fileName,
                    storageMetadata,
                    "Blocks-Template-Generated-Files");

                if (!saveSuccess)
                {
                _logger.LogError("GenerateRenderedFileConsumer: Failed to save file to storage for FileId={FileId}", @event.FileId);
                    await _notificationService.NotifyGenerateRenderedFileEvent(false, @event.FileId, @event.SubscriptionFilterId, @event.ProjectKey);
                    return;
                }

                _logger.LogInformation("GenerateRenderedFileConsumer: File saved successfully to storage for FileId={FileId}", @event.FileId);

                // Step 7: Send success notification
                await _notificationService.NotifyGenerateRenderedFileEvent(true, @event.FileId, @event.SubscriptionFilterId, @event.ProjectKey);

                _logger.LogInformation("GenerateRenderedFileConsumer: Successfully processed GenerateRenderedFileEvent for FileId={FileId}", @event.FileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateRenderedFileConsumer: Exception occurred while processing GenerateRenderedFileEvent for FileId={FileId}", @event.FileId);
                await _notificationService.NotifyGenerateRenderedFileEvent(false, @event.FileId, @event.SubscriptionFilterId, @event.ProjectKey);
            }
        }
    }
}
