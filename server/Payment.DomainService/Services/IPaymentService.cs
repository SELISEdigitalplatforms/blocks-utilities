using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public interface IPaymentService
{
    Task<PaymentOperationResult> MakePaymentAsync(
        MakePaymentRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);

    Task<PaymentOperationResult> GetPaymentAsync(
        string paymentId,
        string correlationId,
        CancellationToken cancellationToken);

    Task RecoverAsync(PaymentDetail payment, CancellationToken cancellationToken);
}
