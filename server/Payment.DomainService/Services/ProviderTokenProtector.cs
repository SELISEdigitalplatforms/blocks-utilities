using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

/// <summary>
/// Protects provider tokens for stored payment methods, adding a stable fingerprint so a token
/// can be matched without being decrypted.
/// </summary>
public sealed class ProviderTokenProtector : IProviderTokenProtector
{
    private readonly IAesGcmSecretProtector _protector;

    public ProviderTokenProtector(IAesGcmSecretProtector protector)
    {
        _protector = protector;
    }

    public bool TryProtect(
        string providerToken,
        out ProtectedProviderToken protectedToken)
    {
        protectedToken = null!;

        if (!_protector.TryProtect(providerToken, out var ciphertext, out var keyId))
        {
            return false;
        }

        protectedToken = new ProtectedProviderToken(
            ciphertext,
            CreateFingerprint(providerToken),
            keyId);

        return true;
    }

    public bool TryUnprotect(
        StoredPaymentMethod method,
        out string providerToken)
    {
        ArgumentNullException.ThrowIfNull(method);

        providerToken = string.Empty;

        if (string.IsNullOrWhiteSpace(method.ProviderTokenCiphertext))
        {
            // Records written before tokens were encrypted still carry the raw value.
            if (string.IsNullOrWhiteSpace(method.StoredPaymentMethodToken))
            {
                return false;
            }

            providerToken = method.StoredPaymentMethodToken;

            return true;
        }

        return _protector.TryUnprotect(
            method.ProviderTokenCiphertext,
            method.TokenEncryptionKeyId ?? string.Empty,
            out providerToken);
    }

    public string CreateFingerprint(string providerToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(providerToken);

        try
        {
            return Convert.ToHexString(SHA256.HashData(tokenBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }
}
