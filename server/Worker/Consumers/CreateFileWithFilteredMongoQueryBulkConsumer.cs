using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Utility.DomainService.TemplateEngine;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class CreateFileWithFilteredMongoQueryBulkConsumer : IConsumer<CreateFileWithFilteredMongoQueryBulkEvent>
    {
        private readonly ILogger<CreateFileWithFilteredMongoQueryBulkConsumer> _logger;
        private readonly StorageHelper _storageHelper;
        private readonly TemplateRenderingService _templateRenderingService;
        private readonly MongoQueryHelper _mongoQueryHelper;
        private readonly ITemplateEngineNotificationService _notificationService;

        public CreateFileWithFilteredMongoQueryBulkConsumer(
            ILogger<CreateFileWithFilteredMongoQueryBulkConsumer> logger,
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

        public async Task Consume(CreateFileWithFilteredMongoQueryBulkEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkConsumer: Processing bulk event with {ItemCount} items, TenantId={TenantId}", @event.DataList?.Count ?? 0, tenantId);

            var successCount = 0;
            var failureCount = 0;

            try
            {
                foreach (var data in @event.DataList ?? new List<CreateFileWithFilteredMongoQueryData>())
                {
                    try
                    {
                        _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkConsumer: Processing item FileId={FileId}", data.FileId);

                        // Step 1: Get template file from storage
                        var templateContent = await _storageHelper.GetFileContentAsString(
                            data.TemplateFileId.ToString(), 
                            @event.ProjectKey ?? tenantId);
                        
                        if (string.IsNullOrEmpty(templateContent))
                        {
                        _logger.LogError("CreateFileWithFilteredMongoQueryBulkConsumer: Template file content is null or empty for TemplateFileId={TemplateFileId}", data.TemplateFileId);
                            failureCount++;
                            continue;
                        }

                        // Step 2: Execute MongoDB queries
                        var entityList = await _mongoQueryHelper.GetEntityListFromQueryData(
                            data.FilteredMongoQueryDatas ?? new List<FilteredMongoQueryData>(), 
                            tenantId);

                        // Step 3: Get connections if needed
                        var connectionsWithEntity = await _mongoQueryHelper.GetConnectionsWithEntityFromData(
                            data.FilteredMongoQueryDatas ?? new List<FilteredMongoQueryData>(), 
                            tenantId);

                        // Step 4: Prepare metadata
                        var metadataDict = MongoQueryHelper.GetMetaDataListFromData(
                            data.MetaDataList ?? new List<MetaData>());

                        // Step 5: Combine all data for rendering
                        var renderData = new Dictionary<string, object>();
                        
                        foreach (var entity in entityList)
                        {
                            renderData[entity.Key] = entity.Value;
                        }

                        foreach (var meta in metadataDict)
                        {
                            if (!renderData.ContainsKey(meta.Key))
                            {
                                renderData[meta.Key] = meta.Value;
                            }
                        }

                        // Step 6: Render template
                        var jsonString = System.Text.Json.JsonSerializer.Serialize(renderData);
                        var renderedContent = _templateRenderingService.RenderTemplateWithJson(templateContent, jsonString);

                        if (string.IsNullOrEmpty(renderedContent))
                        {
                        _logger.LogError("CreateFileWithFilteredMongoQueryBulkConsumer: Rendered content is null or empty for FileId={FileId}", data.FileId);
                            failureCount++;
                            continue;
                        }

                        // Step 7: Save to storage
                        byte[] resultByteArray = Encoding.UTF8.GetBytes(renderedContent);
                        MemoryStream resultStream = new MemoryStream(resultByteArray);

                        var fileNameExtension = data.FileNameExtension.Trim();
                        var fileName = data.FileId + fileNameExtension;

                        var metadata = new Dictionary<string, string>
                        {
                            { "FileId", data.FileId.ToString() },
                            { "TemplateFileId", data.TemplateFileId.ToString() },
                            { "BulkSubscriptionFilterId", @event.SubscriptionFilterId ?? "" },
                            { "CreatedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                            { "QueryCount", (data.FilteredMongoQueryDatas?.Count ?? 0).ToString() },
                            { "FileType", "GeneratedFromMongoQueryBulk" }
                        };

                        var saveSuccess = await _storageHelper.SaveFileToStorage(
                            resultStream,
                            data.FileId.ToString(),
                            fileName,
                            metadata,
                            "Blocks-Template-Mongo-Query-Files");

                        if (saveSuccess)
                        {
                            successCount++;
                            _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkConsumer: Successfully processed FileId={FileId}", data.FileId);
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogError("CreateFileWithFilteredMongoQueryBulkConsumer: Failed to save file for FileId={FileId}", data.FileId);
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.LogError(ex, "CreateFileWithFilteredMongoQueryBulkConsumer: Exception processing item FileId={FileId}", data.FileId);
                    }
                }

                _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkConsumer: Bulk processing completed. Success={SuccessCount}, Failure={FailureCount}", successCount, failureCount);

                // Send notification
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateFileWithFilteredMongoQueryBulkEvent(
                        failureCount == 0, 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        successCount,
                        failureCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateFileWithFilteredMongoQueryBulkConsumer: Exception occurred during bulk processing");
                
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateFileWithFilteredMongoQueryBulkEvent(
                        false, 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        0,
                        @event.DataList.Count);
                }
            }
        }
    }
}
