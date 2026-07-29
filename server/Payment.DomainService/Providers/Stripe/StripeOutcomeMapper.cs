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
