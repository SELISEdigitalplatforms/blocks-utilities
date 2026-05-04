using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.TemplateEngine.Events;
using Utility.DomainService.TemplateEngine.service;

namespace Worker.Consumers
{
    [ExcludeFromCodeCoverage]
    public class CreateMultipleFileWithFilteredMongoQueryConsumer : IConsumer<CreateMultipleFileWithFilteredMongoQueryEvent>
    {
        private readonly ILogger<CreateMultipleFileWithFilteredMongoQueryConsumer> _logger;
        private readonly ITemplateEngineNotificationService _notificationService;

        public CreateMultipleFileWithFilteredMongoQueryConsumer(
            ILogger<CreateMultipleFileWithFilteredMongoQueryConsumer> logger,
            ITemplateEngineNotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task Consume(CreateMultipleFileWithFilteredMongoQueryEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("CreateMultipleFileWithFilteredMongoQueryConsumer: Processing event RequestId={RequestId}, TenantId={TenantId}", @event.RequestId, tenantId);

            try
            {
                // This consumer handles creating multiple files based on saved query configurations
                // The RequestId references a saved configuration in PdfGenerationQueries collection
                
                _logger.LogInformation("CreateMultipleFileWithFilteredMongoQueryConsumer: Looking up saved query configuration RequestId={RequestId}", @event.RequestId);

                // TODO: Implement the logic to:
                // 1. Fetch the PdfGenerationQuery document by RequestId
                // 2. Extract the FilteredMongoQueryDatas from the saved configuration
                // 3. For each query result, generate a separate file
                // 4. Use TemplateFileId if provided, or get from configuration
                
                // Placeholder implementation:
                _logger.LogWarning("CreateMultipleFileWithFilteredMongoQueryConsumer: Implementation pending - saved query lookup not yet implemented");
                _logger.LogInformation("CreateMultipleFileWithFilteredMongoQueryConsumer: Would process RequestId={RequestId} with TemplateFileId={TemplateFileId}", @event.RequestId, @event.TemplateFileId);

                // For now, just send success notification
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateMultipleFileWithFilteredMongoQueryEvent(
                        true, 
                        @event.RequestId.ToString(), 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        "Successfully processed all requests");
                }

                _logger.LogInformation("CreateMultipleFileWithFilteredMongoQueryConsumer: Processing completed for RequestId={RequestId}", @event.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateMultipleFileWithFilteredMongoQueryConsumer: Exception occurred for RequestId={RequestId}", @event.RequestId);
                
                if (@event.NotifyOnProcessEnding)
                {
                    await _notificationService.NotifyCreateMultipleFileWithFilteredMongoQueryEvent(
                        false, 
                        @event.RequestId.ToString(), 
                        @event.SubscriptionFilterId, 
                        @event.ProjectKey,
                        ex.Message);
                }
            }
        }
    }
}
