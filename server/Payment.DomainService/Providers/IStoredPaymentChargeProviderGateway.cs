using Payment.DomainService.Entities;
using Payment.DomainService.Models.StoredPayment;

namespace Payment.DomainService.Providers;

public interface IStoredPaymentChargeProviderGateway
{
    bool Supports(string providerName);

    Task<StoredPaymentChargeProviderResult> ChargeAsync(
        PaymentProvider provider,
        StoredPaymentChargeRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
