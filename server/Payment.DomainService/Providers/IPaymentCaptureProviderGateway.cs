using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;

namespace Payment.DomainService.Providers;

public interface IPaymentCaptureProviderGateway
{
    bool Supports(string providerName);

    Task<PaymentCaptureProviderResult> SubmitAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderCaptureRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
