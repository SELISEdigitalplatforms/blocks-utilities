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
    /// Returns that organization's payments, whichever organization the caller belongs to.
    /// </summary>
    /// <remarks>
    /// This sets the scope; it does not narrow within the caller's own. Any authenticated
    /// caller in the tenant can therefore read any organization's payments by naming one.
    /// That is deliberate — payments are consumed by integrations acting for several
    /// organizations — but it does mean the organization boundary is not enforced on reads.
    /// The tenant still comes from the token, so nothing crosses a tenant.
    /// </remarks>
    public string? OrganizationId { get; set; }
    public string SortBy { get; set; } =
        PaymentQuerySortFields.PaymentDate;
    public string SortDirection { get; set; } =
        PaymentQuerySortDirections.Descending;
    public string? After { get; set; }
    public string? Before { get; set; }
}
