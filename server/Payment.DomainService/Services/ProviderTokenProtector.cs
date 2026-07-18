using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class ProviderTokenProtector : IProviderTokenProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IOptionsMonitor<PaymentOptions> _options;

    public ProviderTokenProtector(
        IOptionsMonitor<PaymentOptions> options)
    {
        _options = options;
    }

    public bool TryProtect(
        string providerToken,
        out ProtectedProviderToken protectedToken)
    {
        protectedToken = null!;

        var options = _options.CurrentValue;
        var keyId = options.ActiveProviderTokenEncryptionKeyId;

        if (string.IsNullOrWhiteSpace(providerToken) ||
            string.IsNullOrWhiteSpace(keyId) ||
            !options.ProviderTokenEncryptionKeys.TryGetValue(
                keyId,
                out var encodedKey) ||
            !TryDecodeKey(encodedKey, out var key))
        {
            return false;
        }

        var plaintext = Encoding.UTF8.GetBytes(providerToken);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(
            ciphertext,
            0,
            payload,
            nonce.Length + tag.Length,
            ciphertext.Length);

        protectedToken = new ProtectedProviderToken(
            Convert.ToBase64String(payload),
            CreateFingerprint(providerToken),
            keyId);

        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(key);

        return true;
    }

    public bool TryUnprotect(
        StoredPaymentMethod method,
        out string providerToken)
    {
        providerToken = string.Empty;

        if (string.IsNullOrWhiteSpace(method.ProviderTokenCiphertext))
        {
            if (string.IsNullOrWhiteSpace(method.StoredPaymentMethodToken))
            {
                return false;
            }

            providerToken = method.StoredPaymentMethodToken;
            return true;
        }

        if (string.IsNullOrWhiteSpace(method.TokenEncryptionKeyId) ||
            !_options.CurrentValue.ProviderTokenEncryptionKeys.TryGetValue(
                method.TokenEncryptionKeyId,
                out var encodedKey) ||
            !TryDecodeKey(encodedKey, out var key))
        {
            return false;
        }

        try
        {
            var payload = Convert.FromBase64String(
                method.ProviderTokenCiphertext);

            if (payload.Length <= NonceSize + TagSize)
            {
                return false;
            }

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var ciphertext = payload.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            providerToken = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);

            return true;
        }
        catch (CryptographicException)
        {
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

    public string CreateFingerprint(string providerToken) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(providerToken)));

    private static bool TryDecodeKey(
        string encodedKey,
        out byte[] key)
    {
        key = [];

        try
        {
            key = Convert.FromBase64String(encodedKey);
            return key.Length is 16 or 24 or 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
