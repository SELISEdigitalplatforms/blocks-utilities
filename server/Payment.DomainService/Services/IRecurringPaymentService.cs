using Payment.DomainService.Requests;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IRecurringPaymentService
{
    Task<PaymentOperationResult> CreateRecurringPaymentAsync(
        CreateRecurringPaymentRequest request,
        string idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken);
}
