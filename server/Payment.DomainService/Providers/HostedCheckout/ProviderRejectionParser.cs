using System.Text.Json;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Providers.HostedCheckout;

internal static class ProviderRejectionParser
{
    private const int MaximumErrorLength = 16_384;

    public static bool TryGetValidationErrorCode(string? packageError, out string errorCode)
    {
        errorCode = string.Empty;

        if (!TryParse(packageError, out var payload) ||
            payload.Status is not (>= 400 and < 500) ||
            !string.Equals(
                payload.ErrorType,
                "validation",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        errorCode = SanitizeErrorCode(payload.ErrorCode);
        return !string.IsNullOrWhiteSpace(errorCode);
    }

    /// <summary>
    /// A provider's "this merchant account is not set up for that" answer, such as Adyen's
    /// <c>905_1</c> (no acquirer for the card brand and currency).
    /// </summary>
    /// <remarks>
    /// Adyen sends these as HTTP 500, which reads as an outage. It is not one: the same request
    /// fails the same way until someone changes the account, so retrying only repeats it. Kept
    /// apart from <see cref="TryGetValidationErrorCode"/> because that one is deliberately limited
    /// to 4xx, and widening it would change how every other caller reads a 5xx.
    /// </remarks>
    public static bool TryGetConfigurationErrorCode(string? packageError, out string errorCode)
    {
        errorCode = string.Empty;

        if (!TryParse(packageError, out var payload) ||
            payload.Status is not (>= 400 and < 600) ||
            !string.Equals(
                payload.ErrorType,
                "configuration",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        errorCode = SanitizeErrorCode(payload.ErrorCode);
        return !string.IsNullOrWhiteSpace(errorCode);
    }

    private static bool TryParse(string? packageError, out ProviderHttpError payload)
    {
        payload = null!;

        if (string.IsNullOrWhiteSpace(packageError) ||
            packageError.Length > MaximumErrorLength)
        {
            return false;
        }

        var jsonStart = packageError.IndexOf('{');
        var jsonEnd = packageError.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ProviderHttpError>(
                packageError[jsonStart..(jsonEnd + 1)]);

            if (parsed is null)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string SanitizeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "payment_provider_rejected";
        }

        var sanitized = new string(value
            .Where(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_')
            .Take(64)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? "payment_provider_rejected"
            : sanitized;
    }
}
