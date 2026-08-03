using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// AES-GCM envelope encryption over a scope's provider token encryption key ring.
/// </summary>
/// <remarks>
/// GCM is authenticated encryption, so a value altered in storage fails to decrypt rather than
/// decrypting to something else. The stored payload is nonce, then tag, then ciphertext,
/// base64-encoded as one string.
/// <para>
/// The ring is owned by <see cref="IProviderTokenEncryptionKeyRingProvider"/> and shared across
/// callers, so it is never disposed here — only the key copies this class takes are zeroed.
/// </para>
/// </remarks>
public sealed class AesGcmSecretProtector : IAesGcmSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IProviderTokenEncryptionKeyRingProvider _keyRings;

    public AesGcmSecretProtector(
        IProviderTokenEncryptionKeyRingProvider keyRings)
    {
        _keyRings = keyRings;
    }

    public async Task<SecretProtectionResult> ProtectAsync(
        PaymentEncryptionScope scope,
        string plaintext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return SecretProtectionResult.Failed;
        }

        var keyRing = await _keyRings.GetAsync(scope, cancellationToken);
        var activeKeyId = keyRing.ActiveKeyId;

        if (string.IsNullOrWhiteSpace(activeKeyId) ||
            !keyRing.TryGetKey(activeKeyId, out var key))
        {
            return SecretProtectionResult.Failed;
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var encrypted = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintextBytes, encrypted, tag);
            }

            var payload = new byte[nonce.Length + tag.Length + encrypted.Length];

            Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
            Buffer.BlockCopy(
                encrypted,
                0,
                payload,
                nonce.Length + tag.Length,
                encrypted.Length);

            return new SecretProtectionResult(
                true,
                Convert.ToBase64String(payload),
                activeKeyId);
        }
        catch (CryptographicException)
        {
            return SecretProtectionResult.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<SecretReadResult> UnprotectAsync(
        PaymentEncryptionScope scope,
        string ciphertext,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) ||
            string.IsNullOrWhiteSpace(keyId))
        {
            return SecretReadResult.Failed;
        }

        var keyRing = await _keyRings.GetAsync(scope, cancellationToken);

        if (!keyRing.TryGetKey(keyId, out var key))
        {
            return SecretReadResult.Failed;
        }

        try
        {
            var payload = Convert.FromBase64String(ciphertext);

            if (payload.Length <= NonceSize + TagSize)
            {
                return SecretReadResult.Failed;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var encrypted = payload.AsSpan(NonceSize + TagSize);
            var plaintextBytes = new byte[encrypted.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, encrypted, tag, plaintextBytes);
            }

            var plaintext = Encoding.UTF8.GetString(plaintextBytes);
            CryptographicOperations.ZeroMemory(plaintextBytes);

            return new SecretReadResult(true, plaintext);
        }
        catch (CryptographicException)
        {
            // Authentication failure: the payload was altered, or the wrong key was named.
            return SecretReadResult.Failed;
        }
        catch (FormatException)
        {
            return SecretReadResult.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
