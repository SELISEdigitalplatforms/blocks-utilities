using System.Security.Cryptography;
using System.Text;

namespace Payment.DomainService.Services;

/// <summary>
/// AES-GCM envelope encryption over the provider token encryption key ring.
/// </summary>
/// <remarks>
/// GCM is authenticated encryption, so a value altered in storage fails to decrypt rather than
/// decrypting to something else. The stored payload is nonce, then tag, then ciphertext,
/// base64-encoded as one string.
/// </remarks>
public sealed class AesGcmSecretProtector : IAesGcmSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IProviderTokenEncryptionKeyRing _keyRing;

    public AesGcmSecretProtector(IProviderTokenEncryptionKeyRing keyRing)
    {
        _keyRing = keyRing;
    }

    public bool TryProtect(
        string plaintext,
        out string ciphertext,
        out string keyId)
    {
        ciphertext = string.Empty;
        keyId = string.Empty;

        var activeKeyId = _keyRing.ActiveKeyId;

        if (string.IsNullOrWhiteSpace(plaintext) ||
            string.IsNullOrWhiteSpace(activeKeyId) ||
            !_keyRing.TryGetKey(activeKeyId, out var key))
        {
            return false;
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

            ciphertext = Convert.ToBase64String(payload);
            keyId = activeKeyId;

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public bool TryUnprotect(
        string ciphertext,
        string keyId,
        out string plaintext)
    {
        plaintext = string.Empty;

        if (string.IsNullOrWhiteSpace(ciphertext) ||
            string.IsNullOrWhiteSpace(keyId) ||
            !_keyRing.TryGetKey(keyId, out var key))
        {
            return false;
        }

        try
        {
            var payload = Convert.FromBase64String(ciphertext);

            if (payload.Length <= NonceSize + TagSize)
            {
                return false;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var encrypted = payload.AsSpan(NonceSize + TagSize);
            var plaintextBytes = new byte[encrypted.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, encrypted, tag, plaintextBytes);
            }

            plaintext = Encoding.UTF8.GetString(plaintextBytes);
            CryptographicOperations.ZeroMemory(plaintextBytes);

            return true;
        }
        catch (CryptographicException)
        {
            // Authentication failure: the payload was altered, or the wrong key was named.
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
