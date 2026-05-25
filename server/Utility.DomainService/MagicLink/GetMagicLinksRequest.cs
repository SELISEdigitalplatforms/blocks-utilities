using Blocks.Genesis;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink
{
    /// <summary>
    /// Request to get a paginated list of magic links
    /// </summary>
    public class GetMagicLinksRequest : IProjectKey
    {
        /// <summary>
        /// Number of items per page (default: 10)
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Page number (0-based index, default: 0)
        /// </summary>
        public int PageNumber { get; set; } = 0;

        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }

        /// <summary>
        /// Optional filter by link type (Action or Redirect)
        /// </summary>
        public MagicLinkType? Type { get; set; }

        /// <summary>
        /// Optional search text (searches Name and Uri)
        /// </summary>
        public string? SearchText { get; set; }

        /// <summary>
        /// Optional filter by status (Active, Expired, UsageLimitExceeded, ManuallyDisabled, TimeExpired)
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Optional filter by RequestMethod (for Action type)
        /// </summary>
        public string? RequestMethod { get; set; }

        /// <summary>
        /// Optional filter by expiry date range
        /// </summary>
        public DateRange? ExpiryDateRange { get; set; }
    }

    /// <summary>
    /// Date range filter
    /// </summary>
    public class DateRange
    {
        /// <summary>
        /// Start date (inclusive)
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// End date (inclusive)
        /// </summary>
        public DateTime? EndDate { get; set; }
    }

    /// <summary>
    /// Response for getting a list of magic links
    /// </summary>
    public class GetMagicLinksResponse : BaseResponse
    {
        /// <summary>
        /// List of magic links with computed status
        /// </summary>
        public List<MagicLinkDto> Data { get; set; } = new();

        /// <summary>
        /// Total count of items matching the filter
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Error message if retrieval failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Request to get a single magic link by ID
    /// </summary>
    public class GetMagicLinkRequest : IProjectKey
    {
        /// <summary>
        /// The unique identifier (short code) of the magic link
        /// </summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Project/tenant key for multi-tenancy
        /// </summary>
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Response for getting a single magic link
    /// </summary>
    public class GetMagicLinkResponse : BaseResponse
    {
        /// <summary>
        /// The magic link data with computed status
        /// </summary>
        public MagicLinkDto? Data { get; set; }

        /// <summary>
        /// Error message if retrieval failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}

