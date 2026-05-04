using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.MagicLink.Models
{
    /// <summary>
    /// Type of magic link - determines behavior when invoked
    /// </summary>
    public enum MagicLinkType
    {
        /// <summary>
        /// Executes an HTTP action when invoked
        /// </summary>
        Action = 0,

        /// <summary>
        /// Redirects to a URL when invoked
        /// </summary>
        Redirect = 1
    }

    /// <summary>
    /// Unified entity for magic links - combines LinkToAction and UrlShortener functionality.
    /// Collection: MagicLinks
    /// </summary>
    [BsonIgnoreExtraElements]
    public class MagicLink : DomainService.MagicLink.MagicLinkData
    {
        /// <summary>
        /// Primary key (short code)
        /// </summary>
        [BsonId]
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Current usage count (number of times the link has been used)
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Calculated expiry date based on CreatedAt + ExpiryLifeSpan.
        /// Null if no expiration (ExpiryLifeSpan = 0).
        /// </summary>
        public DateTime? ExpiryDate { get; set; }

        /// <summary>
        /// Whether the link has expired
        /// </summary>
        public bool IsExpired { get; set; }

        /// <summary>
        /// Reason for expiration if the link is expired
        /// </summary>
        public string? ExpiredReason { get; set; }

        /// <summary>
        /// The project/tenant identifier
        /// </summary>
        public string ProjectKey { get; set; } = string.Empty;

        /// <summary>
        /// The generated short URI (full URL)
        /// </summary>
        public string ShortUri { get; set; } = string.Empty;

        /// <summary>
        /// Language for the action context
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// Origin domain
        /// </summary>
        public string? Origin { get; set; }

        /// <summary>
        /// Created date
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Created by user ID
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Last updated date
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Enum for magic link expiration reasons
    /// </summary>
    public enum MagicLinkExpiredReason
    {
        None = 0,
        UsageLimitExceeded = 1,
        ManuallyDisabled = 2,
        TimeExpired = 3,
        LifespanExpired = 4
    }
}

