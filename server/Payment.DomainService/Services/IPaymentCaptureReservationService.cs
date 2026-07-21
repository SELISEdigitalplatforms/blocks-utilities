using Payment.DomainService.Entities;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureReservationService
{
    Task<PaymentCaptureReservationResult> ReserveAsync(
        PaymentDetail payment,
        PaymentProvider provider,
        CreatePaymentCaptureRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
