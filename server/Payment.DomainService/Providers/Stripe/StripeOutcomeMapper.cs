using System.Text.Json;
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
    /// Maps a non-success response from the error text the HTTP package returns with it.
    /// </summary>
    /// <remarks>
    /// On a non-2xx the package returns the raw response body and nothing else. It logs the
    /// status code but does not return it, so the body is all there is to decide on. A body
    /// carrying Stripe's <c>{"error": {...}}</c> envelope is mapped. Anything else returns false
    /// and leaves the caller's existing fallback in charge.
    /// <para>
    /// Stripe reports an expired or revoked key as a 401 of type <c>api_error</c> with a
    /// <c>code</c>, which <see cref="Map"/> alone would retry forever. Stripe's own 5xx arrive as
    /// <c>api_error</c> without one, so the code is what tells the two apart. Rate limiting
    /// (<c>rate_limit</c>, <c>lock_timeout</c>) comes back as an <c>invalid_request_error</c> and
    /// is the one such error still worth another attempt.
    /// </para>
    /// </remarks>
    public static bool TryMapPackageError(
        string? packageError,
        out ProviderClientOutcome outcome,
        out string? errorCode)
    {
        outcome = ProviderClientOutcome.Failure;
        errorCode = null;

        var error = string.IsNullOrWhiteSpace(packageError)
            ? null
            : ReadError(packageError);
        if (error == null) return false;

        outcome = error switch
        {
            { Code: "rate_limit" or "lock_timeout" } => ProviderClientOutcome.Unavailable,
            { Type: "api_error", Code.Length: > 0 } => ProviderClientOutcome.Rejected,
            _ => Map(error)
        };
        errorCode = SafeCode(error);
        return true;
    }

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
