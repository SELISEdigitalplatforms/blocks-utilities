using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Verifies Stripe's webhook signatures.
/// </summary>
/// <remarks>
/// Stripe signs <c>"{timestamp}.{raw body}"</c> with HMAC-SHA256 and sends the digest
/// hex-encoded. Unlike Adyen it enforces a replay window: the timestamp is inside the signed
/// payload, so it cannot be altered without breaking the signature, and rejecting old
/// timestamps stops a captured request being replayed later. Rolling an endpoint secret keeps
/// the previous one valid for up to 24 hours, during which Stripe sends one signature per
/// active secret, so any match is enough.
/// </remarks>
public sealed class StripeWebhookSignatureVerifier : IWebhookSignatureVerifier
{
    private readonly IOptionsMonitor<PaymentOptions> _options;

    public StripeWebhookSignatureVerifier(IOptionsMonitor<PaymentOptions> options) =>
        _options = options;

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public WebhookSignatureOutcome Verify(
        PaymentProvider provider,
        WebhookSignature signature)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(signature);

        var activeSecret = provider.StandardWebhookHmacKey;
        var previousSecret = provider.PreviousStandardWebhookHmacKey;

        if (string.IsNullOrWhiteSpace(activeSecret))
        {
            return WebhookSignatureOutcome.NotConfigured;
        }

        if (!StripeSignatureHeader.TryParse(
                signature.SuppliedSignature,
                out var timestamp,
                out var candidates))
        {
            return WebhookSignatureOutcome.Invalid;
        }

        var data = Encoding.UTF8.GetBytes(signature.SignedPayload);

        var matched =
            Matches(data, candidates, activeSecret) ||
            !string.IsNullOrWhiteSpace(previousSecret) &&
            Matches(data, candidates, previousSecret);

        if (!matched)
        {
            return WebhookSignatureOutcome.Invalid;
        }

        // Only meaningful once the signature is trusted: an unverified timestamp says nothing.
        return IsWithinTolerance(timestamp)
            ? WebhookSignatureOutcome.Valid
            : WebhookSignatureOutcome.Expired;
    }

    private bool IsWithinTolerance(long timestamp)
    {
        var tolerance = Math.Clamp(
            _options.CurrentValue.StripeSignatureToleranceSeconds,
            30,
            3600);
        var age = Math.Abs(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp);

        return age <= tolerance;
    }

    private static bool Matches(
        byte[] data,
        IReadOnlyCollection<string> candidates,
        string secret)
    {
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data);

        foreach (var candidate in candidates)
        {
            byte[] actual;

            try
            {
                actual = Convert.FromHexString(candidate);
            }
            catch (FormatException)
            {
                continue;
            }

            if (expected.Length == actual.Length &&
                CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Parses the <c>Stripe-Signature</c> header, e.g. <c>t=1492774577,v1=5257a8…</c>.</summary>
internal static class StripeSignatureHeader
{
    private const int MaximumLength = 4_096;

    public static bool TryParse(
        string? header,
        out long timestamp,
        out IReadOnlyCollection<string> signatures)
    {
        timestamp = 0;
        signatures = [];

        if (string.IsNullOrWhiteSpace(header) || header.Length > MaximumLength)
        {
            return false;
        }

        var found = new List<string>();
        var hasTimestamp = false;

        foreach (var part in header.Split(','))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();

            switch (key)
            {
                case "t" when long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed):
                    timestamp = parsed;
                    hasTimestamp = true;
                    break;

                // Only v1 is valid for live events; other schemes are ignored deliberately.
                case "v1" when value.Length > 0:
                    found.Add(value);
                    break;
            }
        }

        signatures = found;

        return hasTimestamp && found.Count > 0;
    }
}
