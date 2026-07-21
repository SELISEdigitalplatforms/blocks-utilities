using Blocks.Genesis;
using FluentValidation;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Request to remove magic links by their IDs
    /// </summary>
    public class RemoveMagicLinksRequest 
    {
        /// <summary>
        /// List of link IDs (short codes) to remove
        /// </summary>
        public List<string> LinkIds { get; set; } = new();
    }

    /// <summary>
    /// Response for removing magic links
    /// </summary>
    public class RemoveMagicLinksResponse : BaseResponse
    {
        /// <summary>
        /// Number of links successfully removed
        /// </summary>
        public int RemovedCount { get; set; }

        /// <summary>
        /// Error message if removal failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Validator for RemoveMagicLinksRequest
    /// </summary>
    public class RemoveMagicLinksRequestValidator : AbstractValidator<RemoveMagicLinksRequest>
    {
        public RemoveMagicLinksRequestValidator()
        {
            RuleFor(x => x.LinkIds)
                .NotNull()
                .WithMessage("LinkIds cannot be null");
        }
    }
}

