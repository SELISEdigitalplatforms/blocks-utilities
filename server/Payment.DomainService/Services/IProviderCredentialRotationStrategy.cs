using Payment.DomainService.Entities;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IProviderCredentialRotationStrategy
{
    bool Supports(string providerName);

    ProviderCredentialRotationPlan CreatePlan(
        PaymentProvider provider,
        RotatePaymentProviderCredentialsRequest request);
}
