namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Service interface for magic link notifications
    /// </summary>
    public interface IMagicLinkNotificationService
    {
        /// <summary>
        /// Notify when a magic link is created
        /// </summary>
        Task NotifyLinkCreatedEvent(bool success, string linkId, string shortUri, string? subscriptionFilterId, string? projectKey);

        /// <summary>
        /// Notify when multiple magic links are created
        /// </summary>
        Task NotifyLinksCreatedEvent(bool success, int successCount, int failureCount, string? subscriptionFilterId, string? projectKey);

        /// <summary>
        /// Notify when magic links are removed
        /// </summary>
        Task NotifyLinksRemovedEvent(bool success, int removedCount, string? subscriptionFilterId, string? projectKey);

        /// <summary>
        /// Notify when an action-type magic link is invoked and executed
        /// </summary>
        Task NotifyActionExecutedEvent(bool success, string linkId, int statusCode, string? errorMessage, string? subscriptionFilterId, string? projectKey);
    }
}

