namespace Utility.DomainService.MagicLink.Events
{
    /// <summary>
    /// Event sent when a magic link is accessed, to track usage and update the count
    /// </summary>
    public class MagicLinkUsageEvent
    {
        /// <summary>
        /// The link ID (short code) that was accessed
        /// </summary>
        public string LinkId { get; set; } = string.Empty;
        
        /// <summary>
        /// The project key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }
        
        /// <summary>
        /// Timestamp when the link was accessed
        /// </summary>
        public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// IP address of the visitor (for logging purposes)
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
    }

    /// <summary>
    /// Event sent to worker to execute an action-type magic link
    /// </summary>
    public record MagicLinkActionEvent
    {
        /// <summary>
        /// The link ID to invoke
        /// </summary>
        public string LinkId { get; set; } = string.Empty;
        
        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }
        
        /// <summary>
        /// Subscription filter ID for notifications
        /// </summary>
        public string? SubscriptionFilterId { get; set; }
        
        /// <summary>
        /// Whether to notify when action completes
        /// </summary>
        public bool NotifyOnProcessEnding { get; set; }
        
        /// <summary>
        /// Whether to raise event when action completes
        /// </summary>
        public bool RaiseEventOnProcessEnding { get; set; }
        
        /// <summary>
        /// Additional reference data for the event
        /// </summary>
        public Dictionary<string, string>? EventReferenceData { get; set; }

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
    }
}

