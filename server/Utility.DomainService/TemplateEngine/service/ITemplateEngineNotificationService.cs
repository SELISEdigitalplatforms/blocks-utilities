namespace Utility.DomainService.TemplateEngine.service
{
    public interface ITemplateEngineNotificationService
    {
        Task NotifyRenderWithJsonEvent(bool success, string renderedFileId, string? subscriptionFilterId, string? projectKey);
        Task NotifyRenderWithJsonBulkEvent(bool success, string referenceId, string? subscriptionFilterId, string? projectKey, int successCount, int failureCount);
        Task NotifyGenerateRenderedFileEvent(bool success, string fileId, string? subscriptionFilterId, string? projectKey);
        Task NotifyGenerateRenderedFilesBulkEvent(bool success, string? bulkSubscriptionFilterId, string? projectKey, int successCount, int failureCount);
        Task NotifyCreateFileWithFilteredMongoQueryEvent(bool success, string fileId, string? subscriptionFilterId, string? projectKey);
        Task NotifyCreateFileWithFilteredMongoQueryBulkEvent(bool success, string? subscriptionFilterId, string? projectKey, int successCount, int failureCount);
        Task NotifyCreateMultipleFileWithFilteredMongoQueryEvent(bool success, string requestId, string? subscriptionFilterId, string? projectKey, string message);
    }
}


