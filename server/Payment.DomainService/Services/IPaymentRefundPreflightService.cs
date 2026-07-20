using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IPaymentRefundPreflightService
{
    Task<PaymentRefundPreflightResult> ExecuteAsync(
        string paymentDetailId,
        CreatePaymentRefundRequest request,
        string idempotencyKey,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
