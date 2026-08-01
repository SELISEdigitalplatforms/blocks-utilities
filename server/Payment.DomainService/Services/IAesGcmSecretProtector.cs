using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Envelope encryption for values that must be stored but not stored in the clear.
/// </summary>
/// <remarks>
/// Keys come from the scope's encryption key ring, which is the one piece of material that
/// still lives in the vault. Ciphertext records the key id that produced it, so a rotated key
/// ring can still read older values.
/// <para>
/// Asynchronous because resolving a scope's ring may be a vault call, and returning records
/// rather than <c>out</c> parameters because C# does not allow <c>out</c> with <c>async</c>.
/// </para>
/// </remarks>
public interface IAesGcmSecretProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the scope's active key.
    /// <see cref="SecretProtectionResult.IsProtected"/> is false when no usable key is
    /// available; callers must treat that as "cannot store this value" rather than storing it
    /// unprotected.
    /// </summary>
    Task<SecretProtectionResult> ProtectAsync(
        PaymentEncryptionScope scope,
        string plaintext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a value produced by <see cref="ProtectAsync"/>.
    /// <see cref="SecretReadResult.IsRead"/> is false when the key is unavailable, the payload
    /// is malformed, or authentication fails — which is what a tampered value looks like.
    /// </summary>
    Task<SecretReadResult> UnprotectAsync(
        PaymentEncryptionScope scope,
        string ciphertext,
        string keyId,
        CancellationToken cancellationToken = default);
}
