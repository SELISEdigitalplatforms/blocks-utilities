using Payment.DomainService.Entities;
using Payment.DomainService.Models;

namespace Payment.DomainService.Providers.HostedCheckout;

/// <summary>Opens a hosted checkout session with one provider.</summary>
public interface IPaymentSessionClient
{
    bool Supports(string providerName);

    Task<ProviderSessionCreationResult> CreateSessionAsync(
        PaymentProvider provider,
        ProviderInitiationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
