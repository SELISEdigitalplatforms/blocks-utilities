namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Notification service interface for PDF generator operations
    /// </summary>
    public interface IPdfGeneratorNotificationService
    {
        Task NotifyMergePdfsEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey);
        Task NotifyCreatePdfsFromHtmlEvent(bool success, string messageCoRelationId, string? projectKey, int successCount, int failureCount);
        Task NotifyExtractTextFromPdfsEvent(bool success, string messageCoRelationId, string? projectKey);
        Task NotifyCreatePdfsFromHtmlUsingTEEvent(bool success, string messageCoRelationId, string? projectKey);
        Task NotifyCreatePdfsFromHtmlUsingTEBulkEvent(bool success, string messageCoRelationId, string? projectKey, int successCount, int failureCount);
        Task NotifyFixPdfsEvent(bool success, string messageCorrelationId, string? projectKey);
        Task NotifyStampImageToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey);
        Task NotifyStampTextToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey);
        Task NotifyStampIntoPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? projectKey);
        Task NotifyConvertDocumentsToPdfEvent(bool success, string messageCoRelationId, string? projectKey, int successCount, int failureCount);
    }
}


