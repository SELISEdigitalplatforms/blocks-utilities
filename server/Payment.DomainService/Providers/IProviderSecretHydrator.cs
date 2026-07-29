using Payment.DomainService.Entities;

namespace Payment.DomainService.Providers;

/// <summary>
/// Resolves one provider's secrets from the vault onto its configuration. Each provider
/// stores a different secret shape, so validation of that shape belongs here rather than in
/// shared code.
/// </summary>
public interface IProviderSecretHydrator
{
    bool Supports(string providerName);

    /// <summary>
    /// Fills the provider's secret fields. Returns <see langword="false"/> when anything is
    /// missing or malformed; callers treat that as "provider unavailable".
    /// </summary>
    Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken);
}
