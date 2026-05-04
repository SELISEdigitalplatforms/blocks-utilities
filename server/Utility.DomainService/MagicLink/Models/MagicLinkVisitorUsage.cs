namespace Utility.DomainService.MagicLink.Models
{
    /// <summary>
    /// Model for storing visitor usage data when a magic link is accessed
    /// </summary>
    public class MagicLinkVisitorUsage
    {
        /// <summary>
        /// Unique identifier for this usage record
        /// </summary>
        public string ItemId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The magic link ID that was accessed
        /// </summary>
        public string LinkId { get; set; } = string.Empty;

        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }

        /// <summary>
        /// IP address of the visitor
        /// </summary>
        public string? VisitorIpAddress { get; set; }

        /// <summary>
        /// User-Agent string of the visitor (browser and OS information)
        /// </summary>
        public string? VisitorUserAgent { get; set; }

        /// <summary>
        /// Origin URL where the visitor came from
        /// </summary>
        public string? VisitorOrigin { get; set; }

        /// <summary>
        /// Visitor's preferred language(s)
        /// </summary>
        public string? VisitorLanguage { get; set; }

        /// <summary>
        /// Type of magic link (Redirect or Action)
        /// </summary>
        public string? LinkType { get; set; }

        /// <summary>
        /// Timestamp when the link was accessed
        /// </summary>
        public DateTime AccessedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the action was successful (for Action type links)
        /// </summary>
        public bool? ActionSuccess { get; set; }

        /// <summary>
        /// HTTP status code returned (for Action type links)
        /// </summary>
        public int? ActionStatusCode { get; set; }

        /// <summary>
        /// Error message if the action failed (for Action type links)
        /// </summary>
        public string? ActionErrorMessage { get; set; }
    }
}
