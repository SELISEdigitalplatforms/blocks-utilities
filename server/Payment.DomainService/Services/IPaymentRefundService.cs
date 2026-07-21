using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentRefundService
{
    Task<PaymentRefundOperationResult> CreatePaymentRefundAsync(
        string paymentDetailId,
        CreatePaymentRefundRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentRefundOperationResult> GetPaymentRefundAsync(
        string paymentDetailId,
        string refundId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<(
        IReadOnlyList<PaymentRefundResponse>? Refunds,
        PaymentRefundOperationResult? Failure)>
        GetPaymentRefundsAsync(
            string paymentDetailId,
            string correlationId,
            CancellationToken cancellationToken);
}
