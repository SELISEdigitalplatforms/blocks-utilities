using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public sealed class PaymentCaptureResponseMapper :
    IPaymentCaptureResponseMapper
{
    public PaymentCaptureResponse Map(
        string paymentDetailId,
        PaymentCapture capture) =>
        new()
        {
            CaptureId = capture.CaptureId,
            PaymentDetailId = paymentDetailId,
            Status = capture.Status,
            Amount = capture.Amount,
            CurrencyCode = capture.CurrencyCode,
            FailureCode = capture.FailureCode,
            CreatedAtUtc = capture.CreatedAtUtc,
            CompletedAtUtc = capture.CompletedAtUtc
        };
}
