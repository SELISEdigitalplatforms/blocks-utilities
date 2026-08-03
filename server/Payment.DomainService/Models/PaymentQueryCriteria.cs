namespace Payment.DomainService.Models;

public sealed record PaymentQueryCriteria
{
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// The calling organization, which narrows the results to its own payments plus the ones
    /// made before organizations existed. Null means the caller belongs to no organization and
    /// sees the whole tenant.
    /// </summary>
    /// <remarks>
    /// Taken from the caller's context, never from the request, so nobody can list another
    /// organization's payments by asking for them.
    /// </remarks>
    public string? OrganizationId { get; init; }
    public int PageSize { get; init; }
    public string[] ProviderNames { get; init; } = [];
    public string[] PaymentStatuses { get; init; } = [];
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public DateTime? PaymentDateFromUtc { get; init; }
    public DateTime? PaymentDateToUtc { get; init; }
    public string? CurrencyCode { get; init; }
    public string? OrderId { get; init; }
    public string? PaymentDetailId { get; init; }
    public string? PaymentFlow { get; init; }
    public string SortBy { get; init; } = string.Empty;
    public string SortDirection { get; init; } = string.Empty;
    public PaymentQueryCursorBoundary? CursorBoundary { get; init; }
    public bool IsBackward { get; init; }
}
