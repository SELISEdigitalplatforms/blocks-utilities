using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class CreateFileWithFilteredMongoQueryConsumer : IConsumer<CreateFileWithFilteredMongoQueryEvent>
    {
        private readonly ILogger<CreateFileWithFilteredMongoQueryConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly MongoQueryHelper _mongoQueryHelper;
        private readonly ITemplateEngineNotificationService _notificationService;

        public CreateFileWithFilteredMongoQueryConsumer(
            ILogger<CreateFileWithFilteredMongoQueryConsumer> logger,
            StorageHelper storageHelper,
            TemplateRenderingService templateRenderingService,
            MongoQueryHelper mongoQueryHelper,
            ITemplateEngineNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _templateRenderingService = templateRenderingService;
            _mongoQueryHelper = mongoQueryHelper;
            _notificationService = notificationService;
        }

        public async Task Consume(CreateFileWithFilteredMongoQueryEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Processing event for FileId={FileId}, TenantId={TenantId}", @event.FileId, tenantId);

            try
            {
                // Step 1: Check if file already exists (optional for now)
                // TODO: Implement duplicate file check if needed

                // Step 2: Get template file from storage
                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Fetching template file TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                var templateContent = await _storageHelper.GetFileContentAsString(
                    @event.TemplateFileId.ToString(), 
                    @event.ProjectKey ?? tenantId);
                
                if (string.IsNullOrEmpty(templateContent))
                {
                _logger.LogError("CreateFileWithFilteredMongoQueryConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", @event.TemplateFileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyCreateFileWithFilteredMongoQueryEvent(
                            false, 
                            @event.FileId.ToString(), 
                            @event.SubscriptionFilterId, 
                            @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Template content fetched successfully, length={Length}", templateContent.Length);

                // Step 3: Execute MongoDB queries to get entity data
                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Executing {QueryCount} MongoDB queries", @event.FilteredMongoQueryDatas?.Count ?? 0);
                var entityList = await _mongoQueryHelper.GetEntityListFromQueryData(
                    @event.FilteredMongoQueryDatas ?? new List<FilteredMongoQueryData>(), 
                    tenantId);

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Got {EntityListCount} entity lists from queries", entityList.Count);

                // Step 4: Get connections if needed
                var connectionsWithEntity = await _mongoQueryHelper.GetConnectionsWithEntityFromData(
                    @event.FilteredMongoQueryDatas ?? new List<FilteredMongoQueryData>(), 
                    tenantId);

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Got {ConnectionGroupCount} connection groups", connectionsWithEntity.Count);

                // Step 5: Prepare metadata
                var metadataDict = MongoQueryHelper.GetMetaDataListFromData(
                    @event.MetaDataList?.ToList() ?? new List<MetaData>());

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Processed {MetadataCount} metadata items", metadataDict.Count);

                // Step 6: Combine all data for rendering
                var renderData = new Dictionary<string, object>();
                
                // Add entity lists
                foreach (var entity in entityList)
                {
                    renderData[entity.Key] = entity.Value;
                }

                // Add metadata
                foreach (var meta in metadataDict)
                {
                    if (!renderData.ContainsKey(meta.Key))
                    {
                        renderData[meta.Key] = meta.Value;
                    }
                }

                // TODO: Add connections to render data if needed
                // For now, connections are fetched but not yet integrated into rendering

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Preparing to render with {RenderDataCount} data items", renderData.Count);

                // Step 7: Render template
                // Convert render data to JSON string for rendering
                var jsonString = System.Text.Json.JsonSerializer.Serialize(renderData);
                
                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Rendering template");
                var renderedContent = _templateRenderingService.RenderTemplateWithJson(templateContent, jsonString);

                if (string.IsNullOrEmpty(renderedContent))
                {
                _logger.LogError("CreateFileWithFilteredMongoQueryConsumer: Rendered content is null or empty for FileId={FileId}", @event.FileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyCreateFileWithFilteredMongoQueryEvent(
                            false, 
                            @event.FileId.ToString(), 
                            @event.SubscriptionFilterId, 
                            @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Template rendered successfully, length={Length}", renderedContent.Length);

                // Step 8: Save to storage
                byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                MemoryStream resultStream = new MemoryStream(resultByteArray);

                var fileNameExtension = @event.FileNameExtension.Trim();
                var fileName = @event.FileId + fileNameExtension;

                var metadata = new Dictionary<string, string>
                {
                    { "FileId", @event.FileId.ToString() },
                    { "TemplateFileId", @event.TemplateFileId.ToString() },
                    { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "QueryCount", (@event.FilteredMongoQueryDatas?.Count ?? 0).ToString() },
                    { "EntityListCount", entityList.Count.ToString() },
                    { "FileType", "GeneratedFromMongoQuery" }
                };

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Saving rendered file to storage with fileName={FileName}", fileName);
                var saveSuccess = await _storageHelper.SaveFileToStorage(
                    resultStream,
                    @event.FileId.ToString(),
                    fileName,
                    metadata,
                    "Blocks-Template-Mongo-Query-Files");

                if (!saveSuccess)
                {
                _logger.LogError("CreateFileWithFilteredMongoQueryConsumer: Failed to save file to storage for FileId={FileId}", @event.FileId);
                    if (@event.NotifyOnProcessEnding)
                    {
                        await _notificationService.NotifyCreateFileWithFilteredMongoQueryEvent(
                            false, 
                            @event.FileId.ToString(), 
                            @event.SubscriptionFilterId, 
                            @event.ProjectKey);
                    }
                    return;
                }

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: File saved successfully to storage for FileId={FileId}", @event.FileId);

                // Step 9: Send success notification
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateFileWithFilteredMongoQueryEvent(
                        true, 
                        @event.FileId.ToString(), 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey);
                }

                _logger.LogInformation("CreateFileWithFilteredMongoQueryConsumer: Successfully processed event for FileId={FileId}", @event.FileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateFileWithFilteredMongoQueryConsumer: Exception occurred while processing event for FileId={FileId}", @event.FileId);
                
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateFileWithFilteredMongoQueryEvent(
                        false, 
                        @event.FileId.ToString(), 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey);
                }
            }
        }
    }
}
