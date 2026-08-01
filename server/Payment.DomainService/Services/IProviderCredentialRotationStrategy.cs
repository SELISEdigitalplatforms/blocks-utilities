using Payment.DomainService.Entities;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Services;

public interface IProviderCredentialRotationStrategy
{
    bool Supports(string providerName);

    Task<ProviderCredentialRotationPlan> CreatePlanAsync(
        PaymentProvider provider,
        RotatePaymentProviderCredentialsRequest request,
        CancellationToken cancellationToken = default);
}
