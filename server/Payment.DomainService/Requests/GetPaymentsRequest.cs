using Payment.DomainService.Enums;

namespace Payment.DomainService.Requests;

public sealed class GetPaymentsRequest
{
    public int PageSize { get; set; } = 25;
    public string[] ProviderNames { get; set; } = [];
    public string[] PaymentStatuses { get; set; } = [];
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public DateTimeOffset? PaymentDateFromUtc { get; set; }
    public DateTimeOffset? PaymentDateToUtc { get; set; }
    public string? CurrencyCode { get; set; }
    public string? OrderId { get; set; }
    public string? PaymentDetailId { get; set; }
    public string? PaymentFlow { get; set; }

    /// <summary>
    /// Narrows the results to one organization.
    /// </summary>
    /// <remarks>
    /// A filter, not a scope. It applies on top of the visibility the caller's context
    /// already grants and cannot reach beyond it: naming an organization the caller cannot
    /// see returns an empty page, not that organization's payments. Useful mainly to a
    /// tenant-level caller, who can already see every organization and wants one at a time.
    /// </remarks>
    public string? OrganizationId { get; set; }
    public string SortBy { get; set; } =
        PaymentQuerySortFields.PaymentDate;
    public string SortDirection { get; set; } =
        PaymentQuerySortDirections.Descending;
    public string? After { get; set; }
    public string? Before { get; set; }
}
