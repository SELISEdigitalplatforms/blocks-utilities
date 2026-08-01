using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

public sealed class StripeCredentialRotationStrategy :
    IProviderCredentialRotationStrategy
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IProviderSecretReader _secretReader;

    public StripeCredentialRotationStrategy(
        IProviderSecretReader secretReader)
    {
        _secretReader = secretReader;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public async Task<ProviderCredentialRotationPlan> CreatePlanAsync(
        PaymentProvider provider,
        RotatePaymentProviderCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        var secrets = await _secretReader.ReadAsync<StripeCredentialSecret>(
            provider,
            cancellationToken);
        var current = secrets.Credentials;
        var tenantSecurity = secrets.TenantSecurity;

        if (!secrets.IsRead || !IsValid(current) || tenantSecurity == null)
        {
            return CredentialsUnavailable();
        }

        if (!IsValidOptionalApiKey(request.ApiKey) ||
            !IsValidOptionalWebhookSecret(request.WebhookHmacKey) ||
            request.TokenHmacKey != null)
        {
            return InvalidCredentials();
        }

        var existing = current!;
        var rotated = new StripeCredentialSecret
        {
            SecretKey = request.ApiKey ?? existing.SecretKey,
            WebhookSigningSecret = Rotate(
                existing.WebhookSigningSecret,
                request.WebhookHmacKey)
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

    private static bool IsValid(StripeCredentialSecret? credentials) =>
        credentials is { WebhookSigningSecret: not null } &&
        IsApiKey(credentials.SecretKey) &&
        IsWebhookSecret(credentials.WebhookSigningSecret.Active) &&
        (string.IsNullOrWhiteSpace(
             credentials.WebhookSigningSecret.Previous) ||
         IsWebhookSecret(
             credentials.WebhookSigningSecret.Previous));

    private static bool IsValidOptionalApiKey(string? value) =>
        value == null || IsApiKey(value);

    private static bool IsValidOptionalWebhookSecret(string? value) =>
        value == null || IsWebhookSecret(value);

    private static bool IsApiKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 8_192 &&
        (value.StartsWith("sk_", StringComparison.Ordinal) ||
         value.StartsWith("rk_", StringComparison.Ordinal));

    private static bool IsWebhookSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 8_192 &&
        value.StartsWith("whsec_", StringComparison.Ordinal);

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
