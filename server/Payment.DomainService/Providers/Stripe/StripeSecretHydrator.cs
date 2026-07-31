using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Resolves Stripe's credentials from the encrypted blobs on the provider document: one API key
/// for every call, and one endpoint signing secret (plus the previous one during a roll).
/// </summary>
/// <remarks>
/// The signing secret is held on the provider's generic webhook-secret fields. Stripe has only
/// one webhook secret, so the second Adyen-shaped slot stays empty.
/// </remarks>
public sealed class StripeSecretHydrator : IProviderSecretHydrator
{
    private const int MaximumSecretValueCharacters = 8_192;
    private const string SigningSecretPrefix = "whsec_";

    private readonly IProviderSecretReader _reader;
    private readonly ILogger<StripeSecretHydrator> _logger;

    public StripeSecretHydrator(
        IProviderSecretReader reader,
        ILogger<StripeSecretHydrator> logger)
    {
        _reader = reader;
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

        var secrets = await _reader.ReadAsync<StripeCredentialSecret>(
            provider,
            cancellationToken);

        if (!secrets.IsRead)
        {
            LogFailure(provider, secrets.FailureReason);

            return false;
        }

        var credentials = secrets.Credentials;
        var tenantSecurity = secrets.TenantSecurity;

        if (!IsValid(credentials) || !IsValid(tenantSecurity))
        {
            LogFailure(provider, "secret_value_invalid");

            return false;
        }

        provider.ApiKey = credentials!.SecretKey;
        provider.StandardWebhookHmacKey = credentials.WebhookSigningSecret.Active;
        provider.PreviousStandardWebhookHmacKey =
            NormalizeOptional(credentials.WebhookSigningSecret.Previous);

        // This service's own material rather than Stripe's: it signs the checkout return state
        // and derives the shopper reference, so every provider needs it.
        provider.ReturnStateHmacKey = tenantSecurity!.ReturnStateHmac.Active;
        provider.PreviousReturnStateHmacKey =
            NormalizeOptional(tenantSecurity.ReturnStateHmac.Previous);
        provider.ShopperReferenceHmacKey = tenantSecurity.ShopperReferenceHmacKey;

        return true;
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

    private static bool IsValidValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumSecretValueCharacters;

    private static bool IsValidSigningSecret(string? value) =>
        IsValidValue(value) &&
        value!.StartsWith(SigningSecretPrefix, StringComparison.Ordinal);

    private static bool IsValidOptionalSigningSecret(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidSigningSecret(value);

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private void LogFailure(PaymentProvider provider, string reason) =>
        _logger.LogError(
            "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash} Reason={Reason}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId),
            reason);
}
