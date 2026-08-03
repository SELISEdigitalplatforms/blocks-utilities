using System.Text.Json;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Providers.HostedCheckout;

internal static class ProviderRejectionParser
{
    private const int MaximumErrorLength = 16_384;

    public static bool TryGetValidationErrorCode(string? packageError, out string errorCode)
    {
        errorCode = string.Empty;

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
            var payload = JsonSerializer.Deserialize<ProviderHttpError>(
                packageError[jsonStart..(jsonEnd + 1)]);

            if (payload?.Status is not (>= 400 and < 500) ||
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
