using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Restricts Stripe calls to Stripe's own API host. Stripe versions through the
/// Stripe-Version header rather than the URL, so there is no path rule to enforce.
/// </summary>
public sealed class StripeEndpointPolicy : IProviderEndpointPolicy
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public bool IsAllowed(string? endpointUrl) =>
        SafeHttpsUrl.TryParse(endpointUrl, out var uri) &&
        uri.Host.Equals(StripeConstants.ApiHost, StringComparison.OrdinalIgnoreCase);
}
