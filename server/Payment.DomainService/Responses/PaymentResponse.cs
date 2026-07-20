using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class PaymentResponse
{
    public string PaymentDetailId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string? OrderId { get; init; }
    public decimal Amount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string? RedirectUrl { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string? CheckoutSessionStatus { get; init; }
    public string? CheckoutResultCode { get; init; }
    public PaymentInstrumentResponse? PaymentInstrument { get; init; }
    public string? PaymentFlow { get; init; }
    public string? RecurringProcessingModel { get; init; }
    public string? CaptureStatus { get; init; }
    public string? CaptureMode { get; init; }
    public decimal AuthorizedAmount { get; init; }
    public decimal CapturedAmount { get; init; }
    public decimal RefundedAmount { get; init; }
}
