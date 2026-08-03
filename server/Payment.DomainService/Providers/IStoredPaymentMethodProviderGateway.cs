using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers;

public interface IStoredPaymentMethodProviderGateway
{
    bool Supports(string providerName);

    Task<StoredPaymentMethodRemovalOutcome> RemoveAsync(
        PaymentProvider provider,
        StoredPaymentMethod method,
        string providerToken,
        CancellationToken cancellationToken);
}
