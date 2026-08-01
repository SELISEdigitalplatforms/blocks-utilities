using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Entities;
using Payment.DomainService.Utilities;

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

    public async Task<ProviderTokenProtectionResult> ProtectAsync(
        PaymentEncryptionScope scope,
        string providerToken,
        CancellationToken cancellationToken = default)
    {
        var protection = await _protector.ProtectAsync(
            scope,
            providerToken,
            cancellationToken);

        if (!protection.IsProtected)
        {
            return ProviderTokenProtectionResult.Failed;
        }

        return new ProviderTokenProtectionResult(
            true,
            new ProtectedProviderToken(
                protection.Ciphertext,
                CreateFingerprint(providerToken),
                protection.KeyId));
    }

    public async Task<ProviderTokenReadResult> UnprotectAsync(
        StoredPaymentMethod method,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (string.IsNullOrWhiteSpace(method.ProviderTokenCiphertext))
        {
            // Records written before tokens were encrypted still carry the raw value.
            return string.IsNullOrWhiteSpace(method.StoredPaymentMethodToken)
                ? ProviderTokenReadResult.Failed
                : new ProviderTokenReadResult(
                    true,
                    method.StoredPaymentMethodToken);
        }

        var read = await _protector.UnprotectAsync(
            PaymentEncryptionScope.From(method),
            method.ProviderTokenCiphertext,
            method.TokenEncryptionKeyId ?? string.Empty,
            cancellationToken);

        return read.IsRead
            ? new ProviderTokenReadResult(true, read.Plaintext)
            : ProviderTokenReadResult.Failed;
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
