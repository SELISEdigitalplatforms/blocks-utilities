using Utility.DomainService.MagicLink.Events;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Service interface for magic link operations (unified UrlShortener + LinkToAction)
    /// </summary>
    public interface IMagicLinkService
    {
        /// <summary>
        /// Creates a single magic link (Redirect or Action type)
        /// </summary>
        Task<CreateMagicLinkResponse> CreateLinkAsync(CreateMagicLinkRequest request);

        /// <summary>
        /// Creates multiple magic links in bulk
        /// </summary>
        Task<CreateMagicLinksResponse> CreateLinksAsync(CreateMagicLinksRequest request);

        /// <summary>
        /// Removes multiple magic links by their IDs
        /// </summary>
        Task<RemoveMagicLinksResponse> RemoveLinksAsync(RemoveMagicLinksRequest request);

        /// <summary>
        /// Gets a single magic link by ID
        /// </summary>
        Task<GetMagicLinkResponse> GetLinkAsync(GetMagicLinkRequest request);

        /// <summary>
        /// Gets a paginated list of magic links with optional filters
        /// </summary>
        Task<GetMagicLinksResponse> GetLinksAsync(GetMagicLinksRequest request);

        /// <summary>
        /// Invokes a magic link - handles both Redirect and Action types.
        /// For Redirect type: sends usage event and returns redirect URL.
        /// For Action type: validates and queues action for background processing.
        /// </summary>
        Task<InvokeMagicLinkResponse> InvokeLinkAsync(InvokeMagicLinkRequest request);

        /// <summary>
        /// Sends a usage tracking event for a magic link
        /// </summary>
        Task SendUsageEventAsync(MagicLinkUsageEvent usageEvent);

        /// <summary>
        /// Sends an action event for background processing (Action type links)
        /// </summary>
        Task SendActionEventAsync(MagicLinkActionEvent actionEvent);

        /// <summary>
        /// Saves (creates or updates) a LinkBasedActionConfig for a project.
        /// Creates a new config if the collection is empty for the project, otherwise updates the existing one.
        /// </summary>
        Task<SaveLinkBasedActionConfigResponse> SaveLinkBasedActionConfigAsync(SaveLinkBasedActionConfigRequest request);

        /// <summary>
        /// Gets the LinkBasedActionConfig for a project
        /// </summary>
        Task<GetLinkBasedActionConfigResponse> GetLinkBasedActionConfigAsync(GetLinkBasedActionConfigRequest request);
    }
}

