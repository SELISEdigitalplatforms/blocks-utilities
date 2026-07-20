using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IRecurringPaymentInitiationService
{
    Task<PaymentOperationResult> InitiateAsync(
        PaymentDetail payment,
        StoredPaymentMethod storedPaymentMethod,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken);

    Task RecoverAsync(
        PaymentDetail payment,
        CancellationToken cancellationToken);
}
