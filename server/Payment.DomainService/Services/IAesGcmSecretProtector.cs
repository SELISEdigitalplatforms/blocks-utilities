namespace Payment.DomainService.Services;

/// <summary>
/// Envelope encryption for values that must be stored but not stored in the clear.
/// </summary>
/// <remarks>
/// Keys come from the provider token encryption key ring, which is the one piece of material
/// that still lives in the vault. Ciphertext records the key id that produced it, so a rotated
/// key ring can still read older values.
/// </remarks>
public interface IAesGcmSecretProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> under the ring's active key. Returns
    /// <see langword="false"/> when no usable key is available; callers must treat that as
    /// "cannot store this value" rather than storing it unprotected.
    /// </summary>
    bool TryProtect(
        string plaintext,
        out string ciphertext,
        out string keyId);

    /// <summary>
    /// Decrypts a value produced by <see cref="TryProtect"/>. Returns <see langword="false"/>
    /// when the key is unavailable, the payload is malformed, or authentication fails — which
    /// is what a tampered value looks like.
    /// </summary>
    bool TryUnprotect(
        string ciphertext,
        string keyId,
        out string plaintext);
}
