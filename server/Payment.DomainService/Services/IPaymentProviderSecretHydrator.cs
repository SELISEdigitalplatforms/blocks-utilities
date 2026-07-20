using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IPaymentProviderSecretHydrator
{
    Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken);
}
