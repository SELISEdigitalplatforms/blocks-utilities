using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureService
{
    Task<PaymentCaptureOperationResult> CreatePaymentCaptureAsync(
        string paymentDetailId,
        CreatePaymentCaptureRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentCaptureOperationResult> GetPaymentCaptureAsync(
        string paymentDetailId,
        string captureId,
        string correlationId,
        CancellationToken cancellationToken);
}
