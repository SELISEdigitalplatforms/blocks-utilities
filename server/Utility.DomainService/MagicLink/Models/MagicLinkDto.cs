namespace Utility.DomainService.MagicLink.Models
{
    /// <summary>
    /// Data transfer object for MagicLink with computed status
    /// </summary>
    public class MagicLinkDto
    {
        /// <summary>
        /// Primary key (short code)
        /// </summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Link type: Action or Redirect
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Friendly name for the link
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Target URL
        /// </summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// Geo-restricted redirect URL
        /// </summary>
        public string? UriOnForbidden { get; set; }

        /// <summary>
        /// HTTP method for Action type
        /// </summary>
        public string? RequestMethod { get; set; }

        /// <summary>
        /// JSON string payload for Action type
        /// </summary>
        public string? RequestPayload { get; set; }

        /// <summary>
        /// JSON string of headers for Action type
        /// </summary>
        public string? RequestHeaders { get; set; }

        /// <summary>
        /// Encoded query string parameters
        /// </summary>
        public string? RequestEncodedQueryString { get; set; }

        /// <summary>
        /// URL to redirect after action is performed
        /// </summary>
        public string? RedirectUrl { get; set; }

        /// <summary>
        /// Maximum number of times the link can be used (0 = unlimited)
        /// </summary>
        public int UsageLimit { get; set; }

        /// <summary>
        /// Current usage count
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Link expiration lifespan in milliseconds
        /// </summary>
        public long ExpiryLifeSpan { get; set; }

        /// <summary>
        /// Calculated expiry date
        /// </summary>
        public DateTime? ExpiryDate { get; set; }

        /// <summary>
        /// Whether the link has expired
        /// </summary>
        public bool IsExpired { get; set; }

        /// <summary>
        /// Reason for expiration
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
        /// User ID that created the link
        /// </summary>
        public string? RequestByUserId { get; set; }

        /// <summary>
        /// Whether user can login when accessing this link
        /// </summary>
        public bool UserCanLogin { get; set; }

        /// <summary>
        /// Client credential string for authentication
        /// </summary>
        public string? ClientCredential { get; set; }

        /// <summary>
        /// Configuration ID used to generate this link
        /// </summary>
        public string? LinkBasedActionConfigId { get; set; }

        /// <summary>
        /// Language for the action context
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// Origin domain
        /// </summary>
        public string? Origin { get; set; }

        /// <summary>
        /// Whether the link is persistent
        /// </summary>
        public bool Persistent { get; set; }

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

        /// <summary>
        /// Computed status of the link: Active, Expired, Disabled, or UsageLimitExceeded
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Maps a MagicLink entity to a MagicLinkDto with computed status
        /// </summary>
        public static MagicLinkDto FromEntity(MagicLink entity)
        {
            var dto = new MagicLinkDto
            {
                ItemId = entity.ItemId,
                Type = entity.Type.ToString(),
                Name = entity.Name,
                Uri = entity.Uri,
                UriOnForbidden = entity.UriOnForbidden,
                RequestMethod = entity.RequestMethod,
                RequestPayload = entity.RequestPayload,
                RequestHeaders = entity.RequestHeaders,
                RequestEncodedQueryString = entity.RequestEncodedQueryString,
                RedirectUrl = entity.RedirectUrl,
                UsageLimit = entity.UsageLimit,
                UsageCount = entity.UsageCount,
                ExpiryLifeSpan = entity.ExpiryLifeSpan,
                ExpiryDate = entity.ExpiryDate,
                IsExpired = entity.IsExpired,
                ExpiredReason = entity.ExpiredReason,
                ProjectKey = entity.ProjectKey,
                ShortUri = entity.ShortUri,
                RequestByUserId = entity.RequestByUserId,
                UserCanLogin = entity.UserCanLogin,
                ClientCredential = entity.ClientCredential,
                LinkBasedActionConfigId = entity.LinkBasedActionConfigId,
                Language = entity.Language,
                Origin = entity.Origin,
                Persistent = entity.Persistent,
                CreatedAt = entity.CreatedAt,
                CreatedBy = entity.CreatedBy,
                UpdatedAt = entity.UpdatedAt,
                Status = CalculateStatus(entity)
            };

            return dto;
        }

        /// <summary>
        /// Calculates the link status based on business rules
        /// </summary>
        public static string CalculateStatus(MagicLink link)
        {
            // 1. Explicitly expired/disabled
            if (link.IsExpired)
            {
                return link.ExpiredReason ?? "Expired";
            }

            // 2. Usage limit exceeded
            if (link.UsageLimit > 0 && link.UsageCount >= link.UsageLimit)
            {
                return MagicLinkExpiredReason.UsageLimitExceeded.ToString();
            }

            // 3. Time expired - use ExpiryDate if available
            if (link.ExpiryDate.HasValue && DateTime.UtcNow > link.ExpiryDate.Value)
            {
                return MagicLinkExpiredReason.TimeExpired.ToString();
            }

            // 4. Fallback: calculate from ExpiryLifeSpan for backward compatibility
            if (link.ExpiryLifeSpan > 0)
            {
                var expiresAt = link.CreatedAt.AddMilliseconds(link.ExpiryLifeSpan);
                if (DateTime.UtcNow > expiresAt)
                {
                    return MagicLinkExpiredReason.LifespanExpired.ToString();
                }
            }

            // 5. Active (no expiry/limits)
            return "Active";
        }
    }
}

