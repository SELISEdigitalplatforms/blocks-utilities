using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IRecurringPaymentReservationService
{
    Task<PaymentReservationResult> ReserveAsync(
        CreateRecurringPaymentRequest request,
        PaymentExecutionContext context,
        string shopperReference,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
