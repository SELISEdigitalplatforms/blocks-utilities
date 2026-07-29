using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

public sealed class AdyenCredentialRotationStrategy :
    IProviderCredentialRotationStrategy
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IProviderSecretReader _secretReader;

    public AdyenCredentialRotationStrategy(
        IProviderSecretReader secretReader)
    {
        _secretReader = secretReader;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public ProviderCredentialRotationPlan CreatePlan(
        PaymentProvider provider,
        RotatePaymentProviderCredentialsRequest request)
    {
        if (!_secretReader.TryRead<ProviderCredentialSecret>(
                provider,
                out var current,
                out var tenantSecurity,
                out _))
        {
            return CredentialsUnavailable();
        }

        if (!IsValid(current) || tenantSecurity == null)
        {
            return CredentialsUnavailable();
        }

        if (!IsValidOptionalApiKey(request.ApiKey) ||
            !IsValidOptionalHmac(request.WebhookHmacKey) ||
            !IsValidOptionalHmac(request.TokenHmacKey))
        {
            return InvalidCredentials();
        }

        var existing = current!;
        var rotated = new ProviderCredentialSecret
        {
            ApiKey = request.ApiKey ?? existing.ApiKey,
            StandardWebhookHmac = Rotate(
                existing.StandardWebhookHmac,
                request.WebhookHmacKey),
            TokenWebhookHmac = Rotate(
                existing.TokenWebhookHmac,
                request.TokenHmacKey)
        };

        return ProviderCredentialRotationPlan.Success(
            JsonSerializer.Serialize(rotated, SerializerOptions),
            JsonSerializer.Serialize(
                tenantSecurity,
                SerializerOptions));
    }

    private static RotatingPaymentSecret Rotate(
        RotatingPaymentSecret current,
        string? requested)
    {
        if (requested == null || SecretsMatch(current.Active, requested))
        {
            return current;
        }

        return new RotatingPaymentSecret
        {
            Active = requested,
            Previous = current.Active
        };
    }

    private static bool IsValid(ProviderCredentialSecret? credentials) =>
        credentials is
        {
            StandardWebhookHmac: not null,
            TokenWebhookHmac: not null
        } &&
        !string.IsNullOrWhiteSpace(credentials.ApiKey) &&
        IsHmac(credentials.StandardWebhookHmac.Active) &&
        IsOptionalHmac(credentials.StandardWebhookHmac.Previous) &&
        IsHmac(credentials.TokenWebhookHmac.Active) &&
        IsOptionalHmac(credentials.TokenWebhookHmac.Previous);

    private static bool IsValidOptionalApiKey(string? value) =>
        value == null ||
        (!string.IsNullOrWhiteSpace(value) && value.Length <= 8_192);

    private static bool IsValidOptionalHmac(string? value) =>
        value == null || IsHmac(value);

    private static bool IsOptionalHmac(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsHmac(value);

    private static bool IsHmac(string? value) =>
        value is { Length: 64 } &&
        value.All(Uri.IsHexDigit);

    private static bool SecretsMatch(string current, string requested)
    {
        var currentHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(current));
        var requestedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(requested));

        return CryptographicOperations.FixedTimeEquals(
            currentHash,
            requestedHash);
    }

    private static ProviderCredentialRotationPlan InvalidCredentials() =>
        ProviderCredentialRotationPlan.Failure(
            PaymentFailureKind.Validation,
            "payment_provider_credentials_invalid",
            "The provider credentials do not match the required format.");

    private static ProviderCredentialRotationPlan CredentialsUnavailable() =>
        ProviderCredentialRotationPlan.Failure(
            PaymentFailureKind.Unavailable,
            "payment_provider_credentials_unavailable",
            "The provider credentials could not be rotated.");
}
