using System.Text.Json;
using System.Text.RegularExpressions;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed partial class PaymentProviderSecretHydrator :
    IPaymentProviderSecretHydrator
{
    private const int MaximumSecretCharacters = 32_768;
    private const int MaximumSecretValueCharacters = 8_192;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly IVault _vault;
    private readonly ILogger<PaymentProviderSecretHydrator> _logger;

    public PaymentProviderSecretHydrator(
        IVault vault,
        ILogger<PaymentProviderSecretHydrator> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    public async Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidSecretName(
                provider.ProviderCredentialSecretName) ||
            !IsValidSecretName(
                provider.TenantSecuritySecretName))
        {
            LogFailure(provider, "secret_reference_invalid");

            return false;
        }

        try
        {
            var credentialSecretName =
                provider.ProviderCredentialSecretName!;
            var tenantSecretName =
                provider.TenantSecuritySecretName!;
            var secrets = await _vault.ProcessSecretsAsync(
                [credentialSecretName, tenantSecretName]);

            cancellationToken.ThrowIfCancellationRequested();

            if (!TryDeserialize(
                    secrets,
                    credentialSecretName,
                    out ProviderCredentialSecret? credentials) ||
                !TryDeserialize(
                    secrets,
                    tenantSecretName,
                    out TenantPaymentSecuritySecret? tenantSecurity) ||
                !IsValid(credentials) ||
                !IsValid(tenantSecurity))
            {
                LogFailure(provider, "secret_value_invalid");

                return false;
            }

            provider.ApiKey = credentials!.ApiKey;
            provider.StandardWebhookHmacKey =
                credentials.StandardWebhookHmac.Active;
            provider.PreviousStandardWebhookHmacKey =
                NormalizeOptional(
                    credentials.StandardWebhookHmac.Previous);
            provider.TokenWebhookHmacKey =
                credentials.TokenWebhookHmac.Active;
            provider.PreviousTokenWebhookHmacKey =
                NormalizeOptional(
                    credentials.TokenWebhookHmac.Previous);
            provider.ReturnStateHmacKey =
                tenantSecurity!.ReturnStateHmac.Active;
            provider.PreviousReturnStateHmacKey =
                NormalizeOptional(
                    tenantSecurity.ReturnStateHmac.Previous);
            provider.ShopperReferenceHmacKey =
                tenantSecurity.ShopperReferenceHmacKey;

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash}",
                PaymentLogValue.Label(provider.ProviderName),
                PaymentLogValue.Hash(provider.TenantId));

            return false;
        }
    }

    private static bool TryDeserialize<T>(
        IReadOnlyDictionary<string, string> secrets,
        string secretName,
        out T? value)
        where T : class
    {
        value = null;

        if (!secrets.TryGetValue(secretName, out var serialized) ||
            string.IsNullOrWhiteSpace(serialized) ||
            serialized.Length > MaximumSecretCharacters)
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(
                serialized,
                SerializerOptions);

            return value != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValid(
        ProviderCredentialSecret? secret) =>
        secret is
        {
            StandardWebhookHmac: not null,
            TokenWebhookHmac: not null
        } &&
        IsValidRequiredValue(secret.ApiKey) &&
        IsValidHexHmac(
            secret.StandardWebhookHmac.Active) &&
        IsValidOptionalHexHmac(
            secret.StandardWebhookHmac.Previous) &&
        IsValidHexHmac(
            secret.TokenWebhookHmac.Active) &&
        IsValidOptionalHexHmac(
            secret.TokenWebhookHmac.Previous);

    private static bool IsValid(
        TenantPaymentSecuritySecret? secret) =>
        secret is
        {
            ReturnStateHmac: not null
        } &&
        IsValidBase64Hmac(
            secret.ReturnStateHmac.Active) &&
        IsValidOptionalBase64Hmac(
            secret.ReturnStateHmac.Previous) &&
        IsValidBase64Hmac(
            secret.ShopperReferenceHmacKey);

    private static bool IsValidRequiredValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumSecretValueCharacters;

    private static bool IsValidHexHmac(string? value) =>
        value is { Length: 64 } &&
        value.All(Uri.IsHexDigit);

    private static bool IsValidOptionalHexHmac(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        IsValidHexHmac(value);

    private static bool IsValidBase64Hmac(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumSecretValueCharacters)
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidOptionalBase64Hmac(
        string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        IsValidBase64Hmac(value);

    private static bool IsValidSecretName(string? secretName) =>
        !string.IsNullOrWhiteSpace(secretName) &&
        SecretNamePattern().IsMatch(secretName);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value;

    private void LogFailure(
        PaymentProvider provider,
        string reason)
    {
        _logger.LogError(
            "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash} Reason={Reason}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId),
            reason);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,127}$")]
    private static partial Regex SecretNamePattern();
}
