namespace Payment.DomainService.Responses;

public sealed class PaymentRefundResponse
{
    public string RefundId { get; init; } = string.Empty;

    public string PaymentDetailId { get; init; } =
        string.Empty;

    public string Status { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string CurrencyCode { get; init; } =
        string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }
}
