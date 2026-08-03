using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IPaymentCapturePreflightService
{
    Task<PaymentCapturePreflightResult> ExecuteAsync(
        string paymentDetailId,
        CreatePaymentCaptureRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
