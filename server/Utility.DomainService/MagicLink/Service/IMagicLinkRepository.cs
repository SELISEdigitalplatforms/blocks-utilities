using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Repository interface for MagicLink data operations.
    /// All operations use RootTenantId for database access.
    /// ProjectKey filtering is used to ensure project-level data isolation.
    /// </summary>
    public interface IMagicLinkRepository
    {
        /// <summary>
        /// Gets a magic link by ID. ProjectKey is optional for anonymous invocations.
        /// </summary>
        Task<Models.MagicLink?> GetMagicLinkAsync(string itemId, string? projectKey = null);

        /// <summary>
        /// Creates a new magic link
        /// </summary>
        Task<string> CreateMagicLinkAsync(Models.MagicLink link);

        /// <summary>
        /// Updates an existing magic link
        /// </summary>
        Task<bool> UpdateMagicLinkAsync(Models.MagicLink link);

        /// <summary>
        /// Gets multiple magic links by IDs
        /// </summary>
        Task<List<Models.MagicLink>> GetMagicLinksByIdsAsync(List<string> itemIds, string projectKey);

        /// <summary>
        /// Gets paginated list of magic links with optional filters
        /// </summary>
        Task<(List<Models.MagicLink> links, int totalCount)> GetMagicLinksAsync(GetMagicLinksRequest request);

        /// <summary>
        /// Increments usage count for a magic link and returns the updated document
        /// </summary>
        Task<Models.MagicLink?> IncrementUsageCountAsync(string linkId);

        /// <summary>
        /// Marks a magic link as expired with a specific reason
        /// </summary>
        Task<bool> MarkAsExpiredAsync(string linkId, MagicLinkExpiredReason reason);

        /// <summary>
        /// Gets client credentials by ID for authentication (Action type links)
        /// </summary>
        Task<ClientCredential?> GetClientCredentialsAsync(string clientCredentialId, string projectKey);

        /// <summary>
        /// Gets link configuration by ID
        /// </summary>
        Task<LinkBasedActionConfig?> GetLinkConfigAsync(string configId, string projectKey);

        /// <summary>
        /// Creates a visitor usage record for tracking link access
        /// </summary>
        Task CreateVisitorUsageAsync(MagicLinkVisitorUsage visitorUsage);

        #region LinkBasedActionConfig Operations

        /// <summary>
        /// Gets the first LinkBasedActionConfig for a project (if exists)
        /// </summary>
        Task<LinkBasedActionConfig?> GetLinkBasedActionConfigAsync(string projectKey);

        /// <summary>
        /// Creates a new LinkBasedActionConfig
        /// </summary>
        Task<string> CreateLinkBasedActionConfigAsync(LinkBasedActionConfig config);

        /// <summary>
        /// Updates an existing LinkBasedActionConfig
        /// </summary>
        Task<bool> UpdateLinkBasedActionConfigAsync(LinkBasedActionConfig config);

        #endregion
    }
}

