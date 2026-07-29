using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.HostedCheckout;

/// <summary>Reads back the outcome of a hosted checkout session from one provider.</summary>
public interface ICheckoutResultClient
{
    bool Supports(string providerName);

    /// <param name="sessionResult">
    /// The opaque result token from the redirect, or null for providers that do not issue one.
    /// A provider that requires it must reject a null itself.
    /// </param>
    Task<CheckoutResultClientResult> GetAsync(
        PaymentProvider provider,
        string sessionId,
        string? sessionResult,
        CancellationToken cancellationToken);
}
