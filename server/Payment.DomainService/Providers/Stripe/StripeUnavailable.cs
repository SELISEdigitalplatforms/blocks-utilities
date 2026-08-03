namespace Payment.DomainService.Providers.Stripe;

internal static class StripeUnavailable
{
    /// <summary>
    /// Whether a transport-level failure reported by the HTTP package looks transient, in
    /// which case the payment stays recoverable rather than being failed.
    /// </summary>
    public static bool IsTransient(string? error) =>
        error?.Contains("circuit", StringComparison.OrdinalIgnoreCase) == true ||
        error?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true;
}
