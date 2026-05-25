using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Shared properties for magic link data - eliminates duplication between request and entity
    /// </summary>
    public class MagicLinkData
    {
        /// <summary>
        /// Link type: Action (executes HTTP request) or Redirect (simple URL redirect)
        /// </summary>
        [BsonRepresentation(BsonType.String)]
        public MagicLinkType Type { get; set; } = MagicLinkType.Action;

        /// <summary>
        /// Friendly name for the link
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Target URL (redirect URL for Redirect type, API endpoint for Action type)
        /// </summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// Geo-restricted redirect URL (Redirect type only)
        /// </summary>
        public string? UriOnForbidden { get; set; }

        /// <summary>
        /// HTTP method for Action type (GET, POST, PUT, DELETE)
        /// </summary>
        public string? RequestMethod { get; set; }

        /// <summary>
        /// JSON string payload for Action type POST/PUT requests
        /// </summary>
        public string? RequestPayload { get; set; }

        /// <summary>
        /// JSON string of headers to send for Action type
        /// </summary>
        public string? RequestHeaders { get; set; }

        /// <summary>
        /// Encoded query string parameters for Action type
        /// </summary>
        public string? RequestEncodedQueryString { get; set; }

        /// <summary>
        /// URL to redirect after action is performed (Action type only)
        /// </summary>
        public string? RedirectUrl { get; set; }

        /// <summary>
        /// Maximum number of times the link can be used (0 = unlimited)
        /// </summary>
        public int UsageLimit { get; set; }

        /// <summary>
        /// Link expiration lifespan in milliseconds (0 = no expiration)
        /// </summary>
        public long ExpiryLifeSpan { get; set; }

        /// <summary>
        /// User ID that created the link (for Action type)
        /// </summary>
        public string? RequestByUserId { get; set; }

        /// <summary>
        /// Whether user can login when accessing this link (Action type only)
        /// </summary>
        public bool UserCanLogin { get; set; }

        /// <summary>
        /// Client credential string for authentication (Action type only)
        /// </summary>
        public string? ClientCredential { get; set; }

        /// <summary>
        /// Configuration ID used to generate this link
        /// </summary>
        public string? LinkBasedActionConfigId { get; set; }

        /// <summary>
        /// Whether the link is persistent
        /// </summary>
        public bool Persistent { get; set; }
    }
}
