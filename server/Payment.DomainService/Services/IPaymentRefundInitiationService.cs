using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentRefundInitiationService
{
    Task<PaymentRefundOperationResult> SubmitAsync(
        PaymentDetail payment,
        PaymentRefund refund,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken);
}
