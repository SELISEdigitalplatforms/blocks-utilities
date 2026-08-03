namespace Payment.DomainService.Models;

public sealed class PaymentQueryCursorPayload
{
    public int Version { get; init; }
    public string SortBy { get; init; } = string.Empty;
    public string SortDirection { get; init; } = string.Empty;
    public string BoundaryValue { get; init; } = string.Empty;
    public string PaymentDetailId { get; init; } = string.Empty;
    public string FilterFingerprint { get; init; } = string.Empty;
}
