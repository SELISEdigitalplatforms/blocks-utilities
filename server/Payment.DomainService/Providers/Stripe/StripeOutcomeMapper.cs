using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Payment.DomainService.Providers.HostedCheckout;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Translates Stripe failures into the outcomes the payment pipeline already reasons about.
/// </summary>
/// <remarks>
/// The distinction that matters is retryable versus terminal. <c>api_error</c> and rate
/// limiting are Stripe-side and worth another attempt, so they map to Unavailable and leave
/// the payment recoverable. A rejected request or a declined card is terminal, so it maps to
/// Rejected. Only sanitized codes leave this class; Stripe's human-readable message is never
/// surfaced, since it can echo request content.
/// </remarks>
public static class StripeOutcomeMapper
{
    public static ProviderClientOutcome Map(StripeError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Type switch
        {
            "api_error" => ProviderClientOutcome.Unavailable,
            "rate_limit_error" => ProviderClientOutcome.Unavailable,
            "idempotency_error" => ProviderClientOutcome.Rejected,
            "invalid_request_error" => ProviderClientOutcome.Rejected,
            "authentication_error" => ProviderClientOutcome.Rejected,
            "card_error" => ProviderClientOutcome.Rejected,
            _ => ProviderClientOutcome.Failure
        };
    }

    /// <summary>
    /// Maps a non-success response the HTTP package reported only as text, in the form
    /// <c>HTTP request failed with status code 400. Error: {"error": {...}}</c>.
    /// </summary>
    /// <remarks>
    /// Decided by status code rather than by <see cref="Map"/>: Stripe reports an expired key as
    /// a 401 of type <c>api_error</c>, which the type alone would retry forever. Any 4xx other
    /// than 429 is terminal. The same request with the same idempotency key cannot succeed on a
    /// later attempt, and each retry schedules another recovery pass. Returns false for 5xx and
    /// for text that is not in this form, which leaves the caller's existing fallback in charge.
    /// </remarks>
    public static bool TryMapPackageError(
        string? packageError,
        out ProviderClientOutcome outcome,
        out string? errorCode)
    {
        outcome = ProviderClientOutcome.Failure;
        errorCode = null;

        var match = string.IsNullOrWhiteSpace(packageError)
            ? Match.Empty
            : StatusCodePattern.Match(packageError);
        if (!match.Success) return false;

        var statusCode = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (statusCode is < 400 or >= 500) return false;

        var error = ReadError(packageError!);
        outcome = statusCode == 429
            ? ProviderClientOutcome.Unavailable
            : ProviderClientOutcome.Rejected;
        errorCode = error != null
            ? SafeCode(error)
            : $"stripe_http_{statusCode}";
        return true;
    }

    private static readonly Regex StatusCodePattern = new(
        @"status code (\d{3})",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static StripeError? ReadError(string packageError)
    {
        var jsonStart = packageError.IndexOf('{');
        var jsonEnd = packageError.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart) return null;

        try
        {
            return JsonSerializer
                .Deserialize<StripeCheckoutSession>(packageError[jsonStart..(jsonEnd + 1)])?
                .Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The most specific safe code available. A decline code says more than the generic
    /// <c>card_declined</c> it accompanies, so it wins when present.
    /// </summary>
    public static string? SafeCode(StripeError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return ProviderRejectionParser.SanitizeErrorCode(
            error.DeclineCode ?? error.Code ?? error.Type);
    }
}
