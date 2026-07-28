using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.HostedCheckout;

/// <summary>Reads back the outcome of a hosted checkout session from one provider.</summary>
public interface ICheckoutResultClient
{
    bool Supports(string providerName);

    Task<CheckoutResultClientResult> GetAsync(
        PaymentProvider provider,
        string sessionId,
        string sessionResult,
        CancellationToken cancellationToken);
}
