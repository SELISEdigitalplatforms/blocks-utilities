using Blocks.Genesis;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Request to save (create or update) a LinkBasedActionConfig.
    /// Creates a new config if the collection is empty, otherwise updates the existing one.
    /// </summary>
    public class SaveLinkBasedActionConfigRequest : IProjectKey
    {
        /// <summary>
        /// Context name for the configuration
        /// </summary>
        public string ContextName { get; set; } = string.Empty;

        /// <summary>
        /// Base URL for generating short URLs (e.g., https://short.example.com/)
        /// </summary>
        public string ShortUrlBase { get; set; } = string.Empty;

        /// <summary>
        /// The project/tenant identifier
        /// </summary>
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Response for saving a LinkBasedActionConfig
    /// </summary>
    public class SaveLinkBasedActionConfigResponse : BaseResponse
    {
        /// <summary>
        /// The config ID (ItemId) of the saved configuration
        /// </summary>
        public string ConfigId { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether this was a create operation (true) or update operation (false)
        /// </summary>
        public bool WasCreated { get; set; }

        /// <summary>
        /// The saved configuration data
        /// </summary>
        public LinkBasedActionConfig? Config { get; set; }

        /// <summary>
        /// Error message if the operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Request to get the LinkBasedActionConfig for a project
    /// </summary>
    public class GetLinkBasedActionConfigRequest : IProjectKey
    {
        /// <summary>
        /// The project/tenant identifier
        /// </summary>
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Response for getting a LinkBasedActionConfig
    /// </summary>
    public class GetLinkBasedActionConfigResponse : BaseResponse
    {
        /// <summary>
        /// The configuration data (null if not found)
        /// </summary>
        public LinkBasedActionConfig? Config { get; set; }

        /// <summary>
        /// Error message if the operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
