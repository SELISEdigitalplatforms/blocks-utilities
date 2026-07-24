namespace Payment.DomainService.Models;

public sealed class PaymentQueryRecord
{
    public string PaymentDetailId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public DateTime PaymentDateUtc { get; init; }
    public string PaymentStatus { get; init; } = string.Empty;
    public bool HasPendingRefund { get; init; }
}
