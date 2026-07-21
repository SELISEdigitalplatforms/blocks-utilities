using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;

namespace Payment.DomainService.Services;

public interface IPaymentCaptureRequestFactory
{
    ProviderCaptureRequest Create(
        PaymentCapture capture,
        long minorUnits);
}
