using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.Utilities;

namespace Utility.DomainService.TemplateEngine.service
{
    /// <summary>
    /// Service implementation for template engine operations
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class TemplateEngineService : ITemplateEngineService
    {
        private readonly ILogger<TemplateEngineService> _logger;
        private readonly IMessageClient _messageClient;

        public TemplateEngineService(
            ILogger<TemplateEngineService> logger,
            IMessageClient messageClient)
        {
            _logger = logger;
            _messageClient = messageClient;
        }

        public async Task<RenderWithJsonResponse> RenderWithJsonAsync(RenderWithJsonRequest request)
        {
            try
            {
                _logger.LogInformation("RenderWithJsonAsync started for RenderedFileId: {RenderedFileId}", request.RenderedFileId);

                // Validate JSON
                if (!IsValidJson(request.JSONString))
                {
                    return new RenderWithJsonResponse
                    {
                        IsSuccess = false,
                        Message = "Invalid JSON string provided"
                    };
                }

                // Send event to worker for async processing
                await SendRenderWithJsonEvent(request);

                _logger.LogInformation("RenderWithJsonAsync event sent for RenderedFileId: {RenderedFileId}", request.RenderedFileId);

                return new RenderWithJsonResponse
                {
                    IsSuccess = true,
                    RenderedFileId = request.RenderedFileId,
                    Message = "Render request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RenderWithJsonAsync for RenderedFileId: {RenderedFileId}", request.RenderedFileId);
                return new RenderWithJsonResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<RenderWithJsonBulkResponse> RenderWithJsonBulkAsync(RenderWithJsonBulkRequest request)
        {
            try
            {
                _logger.LogInformation("RenderWithJsonBulkAsync started for ReferenceId: {ReferenceId}", request.ReferenceId);

                // Validate all JSON strings
                foreach (var payload in request.Payloads)
                {
                    if (!IsValidJson(payload.JSONString))
                    {
                        return new RenderWithJsonBulkResponse
                        {
                            IsSuccess = false,
                            Message = $"Invalid JSON string in payload for RenderedFileId: {payload.RenderedFileId}"
                        };
                    }
                }

                // Send event to worker
                await SendRenderWithJsonBulkEvent(request);

                _logger.LogInformation("RenderWithJsonBulkAsync event sent for ReferenceId: {ReferenceId}", request.ReferenceId);

                return new RenderWithJsonBulkResponse
                {
                    IsSuccess = true,
                    ReferenceId = request.ReferenceId,
                    Message = "Bulk render request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RenderWithJsonBulkAsync for ReferenceId: {ReferenceId}", request.ReferenceId);
                return new RenderWithJsonBulkResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<GenerateRenderedFileResponse> GenerateRenderedFileAsync(GenerateRenderedFileRequest request)
        {
            try
            {
                _logger.LogInformation("GenerateRenderedFileAsync started for FileId: {FileId}", request.FileId);

                // Send event to worker
                await SendGenerateRenderedFileEvent(request);

                _logger.LogInformation("GenerateRenderedFileAsync event sent for FileId: {FileId}", request.FileId);

                return new GenerateRenderedFileResponse
                {
                    IsSuccess = true,
                    FileId = request.FileId,
                    Message = "Generate request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GenerateRenderedFileAsync for FileId: {FileId}", request.FileId);
                return new GenerateRenderedFileResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<GenerateRenderedFilesBulkResponse> GenerateRenderedFilesBulkAsync(GenerateRenderedFilesBulkRequest request)
        {
            try
            {
                _logger.LogInformation("GenerateRenderedFilesBulkAsync started with {Count} requests", request.GenerateRenderedFileRequests.Count);

                // Send event to worker
                await SendGenerateRenderedFilesBulkEvent(request);

                _logger.LogInformation("GenerateRenderedFilesBulkAsync event sent");

                return new GenerateRenderedFilesBulkResponse
                {
                    IsSuccess = true,
                    Message = "Bulk generate request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GenerateRenderedFilesBulkAsync");
                return new GenerateRenderedFilesBulkResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreateFileWithFilteredMongoQueryResponse> CreateFileWithFilteredMongoQueryAsync(CreateFileWithFilteredMongoQueryRequest request)
        {
            try
            {
                _logger.LogInformation("CreateFileWithFilteredMongoQueryAsync started for FileId: {FileId}", request.FileId);

                // Send event to worker
                await SendCreateFileWithFilteredMongoQueryEvent(request);

                _logger.LogInformation("CreateFileWithFilteredMongoQueryAsync event sent for FileId: {FileId}", request.FileId);

                return new CreateFileWithFilteredMongoQueryResponse
                {
                    IsSuccess = true,
                    FileId = request.FileId.ToString(),
                    Message = "Create file request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateFileWithFilteredMongoQueryAsync for FileId: {FileId}", request.FileId);
                return new CreateFileWithFilteredMongoQueryResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreateFileWithFilteredMongoQueryBulkResponse> CreateFileWithFilteredMongoQueryBulkAsync(CreateFileWithFilteredMongoQueryBulkRequest request)
        {
            try
            {
                _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkAsync started");

                // Send event to worker
                await SendCreateFileWithFilteredMongoQueryBulkEvent(request);

                _logger.LogInformation("CreateFileWithFilteredMongoQueryBulkAsync event sent");

                return new CreateFileWithFilteredMongoQueryBulkResponse
                {
                    IsSuccess = true,
                    Message = "Bulk create file request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateFileWithFilteredMongoQueryBulkAsync");
                return new CreateFileWithFilteredMongoQueryBulkResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public async Task<CreateMultipleFileWithFilteredMongoQueryResponse> CreateMultipleFileWithFilteredMongoQueryAsync(CreateMultipleFileWithFilteredMongoQueryRequest request)
        {
            try
            {
                _logger.LogInformation("CreateMultipleFileWithFilteredSqlQueryAsync started for RequestId: {RequestId}", request.RequestId);

                // Send event to worker
                await SendCreateMultipleFileWithFilteredMongoQueryEvent(request);

                _logger.LogInformation("CreateMultipleFileWithFilteredSqlQueryAsync event sent for RequestId: {RequestId}", request.RequestId);

                return new CreateMultipleFileWithFilteredMongoQueryResponse
                {
                    IsSuccess = true,
                    Message = "Create multiple files request queued successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateMultipleFileWithFilteredSqlQueryAsync for RequestId: {RequestId}", request.RequestId);
                return new CreateMultipleFileWithFilteredMongoQueryResponse
                {
                    IsSuccess = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        #region Private Helper Methods - Send Events

        private async Task SendRenderWithJsonEvent(RenderWithJsonRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<RenderWithJsonEvent>
                {
                    ConsumerName = TemplateEngineConstants.RenderWithJsonQueue,
                    Payload = new RenderWithJsonEvent
                    {
                        TemplateFileId = request.TemplateFileId,
                        RenderedFileId = request.RenderedFileId,
                        JSONString = request.JSONString,
                        FileNameExtension = request.FileNameExtension,
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey = BlocksContext.GetContext().TenantId,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendRenderWithJsonBulkEvent(RenderWithJsonBulkRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<RenderWithJsonBulkEvent>
                {
                    ConsumerName = TemplateEngineConstants.BulkOperationsQueue,
                    Payload = new RenderWithJsonBulkEvent
                    {
                        ReferenceId = request.ReferenceId,
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey = BlocksContext.GetContext().TenantId,
                        Payloads = request.Payloads,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendGenerateRenderedFileEvent(GenerateRenderedFileRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<GenerateRenderedFileEvent>
                {
                    ConsumerName = TemplateEngineConstants.GenerateRenderedFileQueue,
                    Payload = new GenerateRenderedFileEvent
                    {
                        FileId = request.FileId,
                        TemplateFileId = request.TemplateFileId,
                        FileNameExtension = request.FileNameExtension,
                        EntityIdentifierList = request.EntityIdentifierList.ToList(),
                        MetaDataList = request.MetaDataList.ToList(),
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey = BlocksContext.GetContext().TenantId,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendGenerateRenderedFilesBulkEvent(GenerateRenderedFilesBulkRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<GenerateRenderedFilesBulkEvent>
                {
                    ConsumerName = TemplateEngineConstants.BulkOperationsQueue,
                    Payload = new GenerateRenderedFilesBulkEvent
                    {
                        BulkSubscriptionFilterId = request.BulkSubscriptionFilterId,
                        ProjectKey =BlocksContext.GetContext().TenantId,
                        GenerateRenderedFileRequests = request.GenerateRenderedFileRequests,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendCreateFileWithFilteredMongoQueryEvent(CreateFileWithFilteredMongoQueryRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateFileWithFilteredMongoQueryEvent>
                {
                    ConsumerName = TemplateEngineConstants.FilteredMongoQueryQueue,
                    Payload = new CreateFileWithFilteredMongoQueryEvent
                    {
                        FileId = request.FileId,
                        TemplateFileId = request.TemplateFileId,
                        FileNameExtension = request.FileNameExtension,
                        FilteredMongoQueryDatas = request.FilteredMongoQueryDatas,
                        MetaDataList = request.MetaDataList.ToList(),
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey =BlocksContext.GetContext().TenantId,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendCreateFileWithFilteredMongoQueryBulkEvent(CreateFileWithFilteredMongoQueryBulkRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateFileWithFilteredMongoQueryBulkEvent>
                {
                    ConsumerName = TemplateEngineConstants.BulkOperationsQueue,
                    Payload = new CreateFileWithFilteredMongoQueryBulkEvent
                    {
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey = BlocksContext.GetContext().TenantId,
                        DataList = request.DataList,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        private async Task SendCreateMultipleFileWithFilteredMongoQueryEvent(CreateMultipleFileWithFilteredMongoQueryRequest request)
        {
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateMultipleFileWithFilteredMongoQueryEvent>
                {
                    ConsumerName = TemplateEngineConstants.FilteredMongoQueryQueue,
                    Payload = new CreateMultipleFileWithFilteredMongoQueryEvent
                    {
                        RequestId = request.RequestId,
                        TemplateFileId = request.TemplateFileId,
                        FileNameExtension = request.FileNameExtension,
                        SubscriptionFilterId = request.SubscriptionFilterId,
                        ProjectKey = BlocksContext.GetContext().TenantId,
                        NotifyOnProcessEnding = request.NotifyOnProcessEnding,
                        RaiseEventOnProcessEnding = request.RaiseEventOnProcessEnding,
                        EventReferenceData = request.EventReferenceData
                    }
                }
            );
        }

        #endregion

        private static bool IsValidJson(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return false;

            jsonString = jsonString.Trim();
            if ((jsonString.StartsWith("{") && jsonString.EndsWith("}")) ||
                (jsonString.StartsWith("[") && jsonString.EndsWith("]")))
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(jsonString);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }
    }
}


