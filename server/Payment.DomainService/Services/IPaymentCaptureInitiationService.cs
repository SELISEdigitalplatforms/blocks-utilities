using Payment.DomainService.Entities;
using Payment.DomainService.Responses;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureInitiationService
{
    Task<PaymentCaptureOperationResult> SubmitAsync(
        PaymentDetail payment,
        PaymentCapture capture,
        PaymentProvider provider,
        string leaseId,
        long minorUnits,
        string correlationId,
        CancellationToken cancellationToken);
}
