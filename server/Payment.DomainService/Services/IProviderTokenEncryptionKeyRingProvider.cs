using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Resolves the encryption key ring for a tenant and organization.
/// </summary>
/// <remarks>
/// Replaces the single ring loaded once at startup. A ring is fetched from the vault on first
/// use and cached, because the vault is a network call on every encrypt and decrypt otherwise.
/// A scope whose ring is missing or malformed resolves to an unusable ring rather than throwing,
/// so one organization's broken key fails only that organization's payments.
/// </remarks>
public interface IProviderTokenEncryptionKeyRingProvider
{
    /// <summary>
    /// The ring protecting <paramref name="scope"/>, or an unusable ring when it cannot be read.
    /// The returned ring stays owned by the provider — callers must not dispose it.
    /// </summary>
    ValueTask<IProviderTokenEncryptionKeyRing> GetAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads a scope's ring from the vault, bypassing the cache. Used by the readiness
    /// diagnostic so an operator can confirm a newly provisioned ring without waiting out the
    /// cache window.
    /// </summary>
    ValueTask<PaymentKeyRingHealth> CheckAsync(
        PaymentEncryptionScope scope,
        CancellationToken cancellationToken = default);
}
