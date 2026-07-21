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

    public string Operation { get; init; } = string.Empty;

    public string? CompletionAction { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureSummary { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }
}
