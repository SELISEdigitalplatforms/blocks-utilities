using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers.Stripe;

public static class StripeRequestHeaders
{
    /// <summary>
    /// Bearer auth plus a pinned API version, and an idempotency key so a retried call after
    /// a timeout returns the original object rather than creating a second one.
    /// </summary>
    public static Dictionary<string, string> Create(
        PaymentProvider provider,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] =
                $"{StripeConstants.AuthorizationScheme} {provider.ApiKey}",
            [StripeConstants.VersionHeader] = StripeConstants.ApiVersion,
            [StripeConstants.IdempotencyHeader] = idempotencyKey
        };
    }
}
