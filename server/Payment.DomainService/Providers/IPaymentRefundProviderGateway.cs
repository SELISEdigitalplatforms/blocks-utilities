using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;

namespace Payment.DomainService.Providers;

public interface IPaymentRefundProviderGateway
{
    bool Supports(string providerName);

    Task<PaymentRefundProviderResult> SubmitAsync(
        PaymentProvider provider,
        string originalPaymentPspReference,
        ProviderRefundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
