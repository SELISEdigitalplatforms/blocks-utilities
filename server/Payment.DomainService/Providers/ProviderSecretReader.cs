using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Services;

namespace Payment.DomainService.Providers;

/// <summary>
/// Decrypts the two credential blobs stored on a provider document. Every provider stores the
/// same pair — its own credentials and this service's security material — so only the
/// credential shape differs, which is the type parameter.
/// </summary>
public sealed class ProviderSecretReader : IProviderSecretReader
{
    private const int MaximumSecretCharacters = 32_768;

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IAesGcmSecretProtector _protector;

    public ProviderSecretReader(IAesGcmSecretProtector protector)
    {
        _protector = protector;
    }

    public bool TryRead<TCredential>(
        PaymentProvider provider,
        out TCredential? credentials,
        out TenantPaymentSecuritySecret? tenantSecurity,
        out string failureReason)
        where TCredential : class
    {
        ArgumentNullException.ThrowIfNull(provider);

        credentials = null;
        tenantSecurity = null;

        if (string.IsNullOrWhiteSpace(provider.SecretsEncryptionKeyId) ||
            string.IsNullOrWhiteSpace(provider.ProviderSecretsCiphertext) ||
            string.IsNullOrWhiteSpace(provider.TenantSecuritySecretsCiphertext))
        {
            failureReason = "secrets_missing";

            return false;
        }

        if (!TryDecrypt(
                provider.ProviderSecretsCiphertext,
                provider.SecretsEncryptionKeyId,
                out credentials) ||
            !TryDecrypt(
                provider.TenantSecuritySecretsCiphertext,
                provider.SecretsEncryptionKeyId,
                out tenantSecurity))
        {
            // Covers an unavailable key, a tampered payload, and unparseable JSON alike:
            // none of them can produce usable credentials.
            failureReason = "secrets_unreadable";

            return false;
        }

        failureReason = string.Empty;

        return true;
    }

    private bool TryDecrypt<T>(
        string ciphertext,
        string keyId,
        out T? value)
        where T : class
    {
        value = null;

        if (!_protector.TryUnprotect(ciphertext, keyId, out var json) ||
            string.IsNullOrWhiteSpace(json) ||
            json.Length > MaximumSecretCharacters)
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);

            return value != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
