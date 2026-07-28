namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Fixed values from Stripe's HTTP API. Verified against docs.stripe.com, July 2026.
/// </summary>
public static class StripeConstants
{
    public const string ApiHost = "api.stripe.com";

    /// <summary>
    /// Pinned request API version. Stripe is date-versioned, so pinning keeps response parsing
    /// stable when Stripe rolls its default forward.
    /// </summary>
    /// <remarks>
    /// Keep this aligned with the version configured on the webhook endpoint. Requests use
    /// this header, but webhook payloads are rendered with the endpoint's own version, so
    /// letting the two drift apart means sending and receiving different shapes.
    /// </remarks>
    public const string ApiVersion = "2026-06-24.dahlia";

    public const string AuthorizationScheme = "Bearer";
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string VersionHeader = "Stripe-Version";
    public const string SignatureHeader = "Stripe-Signature";

    /// <summary>Stripe's own default replay window for webhook timestamps.</summary>
    public const int SignatureToleranceSeconds = 300;

    /// <summary>Name of the single webhook signing secret configured per endpoint.</summary>
    public const string WebhookSecretName = "endpoint";

    /// <summary>Stripe caps client_reference_id at 200 characters.</summary>
    public const int MaximumClientReferenceLength = 200;
}
