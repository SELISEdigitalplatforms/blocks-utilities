using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

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

    public async Task<ProviderSecretReadResult<TCredential>> ReadAsync<TCredential>(
        PaymentProvider provider,
        CancellationToken cancellationToken = default)
        where TCredential : class
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.SecretsEncryptionKeyId) ||
            string.IsNullOrWhiteSpace(provider.ProviderSecretsCiphertext) ||
            string.IsNullOrWhiteSpace(provider.TenantSecuritySecretsCiphertext))
        {
            return ProviderSecretReadResult<TCredential>.Failed(
                "secrets_missing");
        }

        var scope = PaymentEncryptionScope.From(provider);

        var credentials = await DecryptAsync<TCredential>(
            scope,
            provider.ProviderSecretsCiphertext,
            provider.SecretsEncryptionKeyId,
            cancellationToken);
        var tenantSecurity = await DecryptAsync<TenantPaymentSecuritySecret>(
            scope,
            provider.TenantSecuritySecretsCiphertext,
            provider.SecretsEncryptionKeyId,
            cancellationToken);

        if (credentials == null || tenantSecurity == null)
        {
            // Covers an unavailable key, a tampered payload, and unparseable JSON alike:
            // none of them can produce usable credentials.
            return ProviderSecretReadResult<TCredential>.Failed(
                "secrets_unreadable");
        }

        return new ProviderSecretReadResult<TCredential>(
            true,
            credentials,
            tenantSecurity,
            string.Empty);
    }

    private async Task<T?> DecryptAsync<T>(
        PaymentEncryptionScope scope,
        string ciphertext,
        string keyId,
        CancellationToken cancellationToken)
        where T : class
    {
        var read = await _protector.UnprotectAsync(
            scope,
            ciphertext,
            keyId,
            cancellationToken);

        if (!read.IsRead ||
            string.IsNullOrWhiteSpace(read.Plaintext) ||
            read.Plaintext.Length > MaximumSecretCharacters)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                read.Plaintext,
                SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
