using Payment.DomainService.Entities;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IPaymentRefundReservationService
{
    Task<PaymentRefundReservationResult> ReserveAsync(
        PaymentDetail payment,
        PaymentProvider provider,
        CreatePaymentRefundRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
