namespace Payment.DomainService.Providers;

/// <summary>
/// What the system knows about a provider without being told: things that follow from the
/// provider's identity rather than from how a tenant configured it.
/// </summary>
/// <param name="Name">Canonical provider name.</param>
/// <param name="DefaultApiBaseUrl">
/// The provider's fixed API host, when it has one. Stripe versions through a header, so its
/// base URL never varies. Adyen's Checkout host differs per environment and API version, so it
/// has no default and must be supplied and validated against the provider's endpoint policy.
/// </param>
public sealed record PaymentProviderDescriptor(
    string Name,
    string? DefaultApiBaseUrl);
