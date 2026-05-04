using Blocks.Genesis;
using FluentValidation;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Request to create a single magic link
    /// </summary>
    public class CreateMagicLinkRequest : MagicLinkData, IProjectKey
    {
        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Request to create multiple magic links
    /// </summary>
    public class CreateMagicLinksRequest : IProjectKey
    {
        /// <summary>
        /// List of magic link creation requests
        /// </summary>
        public List<CreateMagicLinkRequest> Requests { get; set; } = new();

        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Response for creating a single magic link
    /// </summary>
    public class CreateMagicLinkResponse : BaseResponse
    {
        /// <summary>
        /// The generated link ID (short code)
        /// </summary>
        public string LinkId { get; set; } = string.Empty;

        /// <summary>
        /// The generated short URL
        /// </summary>
        public string ShortUri { get; set; } = string.Empty;

        /// <summary>
        /// The link type
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Error message if creation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Response for creating multiple magic links
    /// </summary>
    public class CreateMagicLinksResponse : BaseResponse
    {
        /// <summary>
        /// List of created links with their IDs
        /// </summary>
        public List<MagicLinkResult> Links { get; set; } = new();

        /// <summary>
        /// Total number of successfully created links
        /// </summary>
        public int TotalSuccessCount { get; set; }

        /// <summary>
        /// Error message if creation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Individual magic link result
    /// </summary>
    public class MagicLinkResult
    {
        /// <summary>
        /// The generated link ID (short code)
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The generated short URL
        /// </summary>
        public string ShortUri { get; set; } = string.Empty;

        /// <summary>
        /// The link type
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Whether the link was created successfully
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Error message if creation failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Validator for CreateMagicLinkRequest
    /// </summary>
    public class CreateMagicLinkRequestValidator : AbstractValidator<CreateMagicLinkRequest>
    {
        public CreateMagicLinkRequestValidator()
        {
            RuleFor(x => x.Uri)
                .NotEmpty()
                .WithMessage("Uri is required")
                .Must(BeAValidUrl)
                .WithMessage("Uri must be a valid URL");

            // Action type requires RequestMethod
            When(x => x.Type == MagicLinkType.Action, () =>
            {
                RuleFor(x => x.RequestMethod)
                    .NotEmpty()
                    .WithMessage("RequestMethod is required for Action type")
                    .Must(BeAValidHttpMethod!)
                    .WithMessage("RequestMethod must be GET, POST, PUT, or DELETE");
            });

            RuleFor(x => x.UsageLimit)
                .GreaterThanOrEqualTo(0)
                .WithMessage("UsageLimit must be 0 (unlimited) or a positive number");

            RuleFor(x => x.ExpiryLifeSpan)
                .GreaterThanOrEqualTo(0)
                .WithMessage("ExpiryLifeSpan must be 0 (no expiry) or a positive number");
        }

        private static bool BeAValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }

        private static bool BeAValidHttpMethod(string method)
        {
            var validMethods = new[] { "GET", "POST", "PUT", "DELETE" };
            return validMethods.Contains(method.ToUpperInvariant());
        }
    }
}

