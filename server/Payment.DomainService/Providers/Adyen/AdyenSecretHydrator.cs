using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Resolves Adyen's credentials from the encrypted blobs on the provider document: an API key
/// and two rotating HMAC keys, one per webhook kind.
/// </summary>
public sealed class AdyenSecretHydrator : IProviderSecretHydrator
{
    private const int MaximumSecretValueCharacters = 8_192;

    private readonly IProviderSecretReader _reader;
    private readonly ILogger<AdyenSecretHydrator> _logger;

    public AdyenSecretHydrator(
        IProviderSecretReader reader,
        ILogger<AdyenSecretHydrator> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public Task<bool> HydrateAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_reader.TryRead<ProviderCredentialSecret>(
                provider,
                out var credentials,
                out var tenantSecurity,
                out var failureReason))
        {
            LogFailure(provider, failureReason);

            return Task.FromResult(false);
        }

        if (!IsValid(credentials) || !IsValid(tenantSecurity))
        {
            LogFailure(provider, "secret_value_invalid");

            return Task.FromResult(false);
        }

        provider.ApiKey = credentials!.ApiKey;
        provider.StandardWebhookHmacKey = credentials.StandardWebhookHmac.Active;
        provider.PreviousStandardWebhookHmacKey =
            NormalizeOptional(credentials.StandardWebhookHmac.Previous);
        provider.TokenWebhookHmacKey = credentials.TokenWebhookHmac.Active;
        provider.PreviousTokenWebhookHmacKey =
            NormalizeOptional(credentials.TokenWebhookHmac.Previous);
        provider.ReturnStateHmacKey = tenantSecurity!.ReturnStateHmac.Active;
        provider.PreviousReturnStateHmacKey =
            NormalizeOptional(tenantSecurity.ReturnStateHmac.Previous);
        provider.ShopperReferenceHmacKey = tenantSecurity.ShopperReferenceHmacKey;

        return Task.FromResult(true);
    }

    private static bool IsValid(ProviderCredentialSecret? secret) =>
        secret is
        {
            StandardWebhookHmac: not null,
            TokenWebhookHmac: not null
        } &&
        IsValidRequiredValue(secret.ApiKey) &&
        IsValidHexHmac(secret.StandardWebhookHmac.Active) &&
        IsValidOptionalHexHmac(secret.StandardWebhookHmac.Previous) &&
        IsValidHexHmac(secret.TokenWebhookHmac.Active) &&
        IsValidOptionalHexHmac(secret.TokenWebhookHmac.Previous);

    private static bool IsValid(TenantPaymentSecuritySecret? secret) =>
        secret is { ReturnStateHmac: not null } &&
        IsValidBase64Hmac(secret.ReturnStateHmac.Active) &&
        IsValidOptionalBase64Hmac(secret.ReturnStateHmac.Previous) &&
        IsValidBase64Hmac(secret.ShopperReferenceHmacKey);

    private static bool IsValidRequiredValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumSecretValueCharacters;

    private static bool IsValidHexHmac(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsValidOptionalHexHmac(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidHexHmac(value);

    private static bool IsValidBase64Hmac(string? value)
    {
        if (!IsValidRequiredValue(value)) return false;

        try
        {
            return Convert.FromBase64String(value!).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidOptionalBase64Hmac(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidBase64Hmac(value);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private void LogFailure(PaymentProvider provider, string reason) =>
        _logger.LogError(
            "Payment provider secrets could not be resolved Provider={Provider} TenantHash={TenantHash} Reason={Reason}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(provider.TenantId),
            reason);
}
