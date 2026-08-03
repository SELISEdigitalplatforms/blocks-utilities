using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

/// <summary>
/// Resolves and validates the URLs this service owns: where the provider sends the browser
/// back, and where the browser is finally redirected. Which endpoints a provider may be
/// *called* at is decided per provider by <see cref="Providers.IProviderEndpointPolicy"/>.
/// </summary>
public interface ICheckoutUrlPolicy
{
    bool TryResolveHostedUrls(PaymentProvider provider, string signedState, out string returnUrl, out string frontendResultUrl);
    bool IsAllowedFrontendResultUrl(string value);
}
