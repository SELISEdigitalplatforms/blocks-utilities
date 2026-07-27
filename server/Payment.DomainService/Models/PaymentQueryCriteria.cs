namespace Payment.DomainService.Models;

public sealed record PaymentQueryCriteria
{
    public string TenantId { get; init; } = string.Empty;
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
