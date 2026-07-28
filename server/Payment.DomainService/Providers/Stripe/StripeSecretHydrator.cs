using System.Text.Json;
using System.Text.RegularExpressions;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Resolves Stripe's secrets: one API key for every call, and one endpoint signing secret
/// (plus the previous one during a roll).
/// </summary>
/// <remarks>
/// The signing secret is stored on the provider's generic webhook-secret fields. Stripe has
/// only one webhook secret, so the second Adyen-shaped slot stays empty.
/// </remarks>
public sealed partial class StripeSecretHydrator : IProviderSecretHydrator
{
    private const int MaximumSecretCharacters = 32_768;
    private const int MaximumSecretValueCharacters = 8_192;
    private const string SigningSecretPrefix = "whsec_";

    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IVault _vault;
    private readonly ILogger<StripeSecretHydrator> _logger;

    public StripeSecretHydrator(
        IVault vault,
        ILogger<StripeSecretHydrator> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValidSecretName(provider.ProviderCredentialSecretName) ||
            !IsValidSecretName(provider.TenantSecuritySecretName))
        {
            LogFailure(provider, "secret_reference_invalid");

            return false;
        }

        try
        {
            var credentialSecretName = provider.ProviderCredentialSecretName!;
            var tenantSecretName = provider.TenantSecuritySecretName!;
            var secrets = await _vault.ProcessSecretsAsync(
                [credentialSecretName, tenantSecretName]);

            cancellationToken.ThrowIfCancellationRequested();

            if (!TryDeserialize(
                    secrets,
                    credentialSecretName,
                    out StripeCredentialSecret? credentials) ||
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

            provider.ApiKey = credentials!.SecretKey;
            provider.StandardWebhookHmacKey = credentials.WebhookSigningSecret.Active;
            provider.PreviousStandardWebhookHmacKey =
                NormalizeOptional(credentials.WebhookSigningSecret.Previous);

            // Tenant security material is this service's own, not Stripe's: it signs the
            // return state and derives the shopper reference, so every provider needs it.
            provider.ReturnStateHmacKey = tenantSecurity!.ReturnStateHmac.Active;
            provider.PreviousReturnStateHmacKey =
                NormalizeOptional(tenantSecurity.ReturnStateHmac.Previous);
            provider.ShopperReferenceHmacKey = tenantSecurity.ShopperReferenceHmacKey;

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
            value = JsonSerializer.Deserialize<T>(serialized, SerializerOptions);

            return value != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValid(StripeCredentialSecret? secret) =>
        secret is { WebhookSigningSecret: not null } &&
        IsValidValue(secret.SecretKey) &&
        IsValidSigningSecret(secret.WebhookSigningSecret.Active) &&
        IsValidOptionalSigningSecret(secret.WebhookSigningSecret.Previous);

    private static bool IsValid(TenantPaymentSecuritySecret? secret) =>
        secret is { ReturnStateHmac: not null } &&
        IsValidBase64Key(secret.ReturnStateHmac.Active) &&
        IsValidOptionalBase64Key(secret.ReturnStateHmac.Previous) &&
        IsValidBase64Key(secret.ShopperReferenceHmacKey);

    private static bool IsValidBase64Key(string? value)
    {
        if (!IsValidValue(value)) return false;

        try
        {
            return Convert.FromBase64String(value!).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidOptionalBase64Key(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidBase64Key(value);

    private static bool IsValidValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumSecretValueCharacters;

    private static bool IsValidSigningSecret(string? value) =>
        IsValidValue(value) &&
        value!.StartsWith(SigningSecretPrefix, StringComparison.Ordinal);

    private static bool IsValidOptionalSigningSecret(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidSigningSecret(value);

    private static bool IsValidSecretName(string? secretName) =>
        !string.IsNullOrWhiteSpace(secretName) &&
        SecretNamePattern().IsMatch(secretName);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private void LogFailure(PaymentProvider provider, string reason) =>
        _logger.LogError(
            "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash} Reason={Reason}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId),
            reason);

    [GeneratedRegex("^[A-Za-z0-9-]{1,127}$")]
    private static partial Regex SecretNamePattern();
}
