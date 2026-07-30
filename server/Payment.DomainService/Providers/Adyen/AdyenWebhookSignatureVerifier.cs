using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Verifies Adyen's HMAC-SHA256 signatures. Both webhook kinds send a base64 digest; they
/// differ in which secret applies and how that secret is encoded. Adyen sets no replay window,
/// so duplicate suppression is left to the inbox deduplication key.
/// </summary>
public sealed class AdyenWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
            StringComparison.OrdinalIgnoreCase);

    public WebhookSignatureOutcome Verify(
        PaymentProvider provider,
        WebhookSignature signature)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(signature);

        var (activeKey, previousKey, keyIsHex) = ResolveSecret(provider, signature.SecretName);

        if (string.IsNullOrWhiteSpace(activeKey))
        {
            return WebhookSignatureOutcome.NotConfigured;
        }

        var data = Encoding.UTF8.GetBytes(signature.SignedPayload);

        var matched =
            Matches(data, signature.SuppliedSignature, activeKey, keyIsHex) ||
            !string.IsNullOrWhiteSpace(previousKey) &&
            Matches(data, signature.SuppliedSignature, previousKey, keyIsHex);

        return matched
            ? WebhookSignatureOutcome.Valid
            : WebhookSignatureOutcome.Invalid;
    }

    private static (string? Active, string? Previous, bool KeyIsHex) ResolveSecret(
        PaymentProvider provider,
        string secretName) =>
        secretName switch
        {
            AdyenWebhookSecrets.Standard => (
                provider.StandardWebhookHmacKey,
                provider.PreviousStandardWebhookHmacKey,
                true),
            AdyenWebhookSecrets.Token => (
                provider.TokenWebhookHmacKey,
                provider.PreviousTokenWebhookHmacKey,
                false),
            _ => (null, null, false)
        };

    private static bool Matches(
        byte[] data,
        string supplied,
        string key,
        bool keyIsHex)
    {
        try
        {
            var keyBytes = keyIsHex
                ? Convert.FromHexString(key)
                : DecodeFlexible(key);
            var expected = HMACSHA256.HashData(keyBytes, data);
            var actual = Convert.FromBase64String(supplied);

            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DecodeFlexible(string key)
    {
        if (key.Length % 2 == 0 && key.All(Uri.IsHexDigit))
        {
            return Convert.FromHexString(key);
        }

        try { return Convert.FromBase64String(key); }
        catch (FormatException) { return Encoding.UTF8.GetBytes(key); }
    }
}
