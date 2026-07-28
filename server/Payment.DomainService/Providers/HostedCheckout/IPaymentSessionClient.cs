using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Providers.HostedCheckout;

/// <summary>Opens a hosted checkout session with one provider.</summary>
public interface IPaymentSessionClient
{
    bool Supports(string providerName);

    Task<ProviderSessionCreationResult> CreateSessionAsync(
        PaymentProvider provider,
        HostedCheckoutSessionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
