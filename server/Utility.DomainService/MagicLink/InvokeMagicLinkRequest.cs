using Blocks.Genesis;
using FluentValidation;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Request to invoke a magic link
    /// </summary>
    public class InvokeMagicLinkRequest : IProjectKey
    {
        /// <summary>
        /// The magic link ID (short code) to invoke
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
        /// Visitor IP address (for logging/geo-restriction)
        /// </summary>
        public string? VisitorIpAddress { get; set; }

        /// <summary>
        /// Visitor User-Agent string (browser and OS information)
        /// </summary>
        public string? VisitorUserAgent { get; set; }

        /// <summary>
        /// Request origin URL (where the visitor came from)
        /// </summary>
        public string? VisitorOrigin { get; set; }

        /// <summary>
        /// Visitor's preferred language(s)
        /// </summary>
        public string? VisitorLanguage { get; set; }
    }

    /// <summary>
    /// Response for invoking a magic link
    /// </summary>
    public class InvokeMagicLinkResponse : BaseResponse
    {
        /// <summary>
        /// Redirect URL (for Redirect type or post-action redirect for Action type)
        /// </summary>
        public string? RedirectUrl { get; set; }

        /// <summary>
        /// Error code if invocation failed
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Error message if invocation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// The link type that was invoked
        /// </summary>
        public string? Type { get; set; }
    }

    /// <summary>
    /// Validator for InvokeMagicLinkRequest
    /// </summary>
    public class InvokeMagicLinkRequestValidator : AbstractValidator<InvokeMagicLinkRequest>
    {
        public InvokeMagicLinkRequestValidator()
        {
            RuleFor(x => x.LinkId)
                .NotEmpty()
                .WithMessage("LinkId is required");
        }
    }
}

