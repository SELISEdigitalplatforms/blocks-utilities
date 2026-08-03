using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Stripe PaymentIntent, as returned by capture and cancel. Both operations answer with the
/// intent in its new state rather than with an operation-specific object.
/// </summary>
public sealed class StripePaymentIntent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// <c>requires_capture</c>, <c>succeeded</c>, <c>canceled</c>, <c>processing</c> and so on.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount")]
    public long? Amount { get; set; }

    /// <summary>What Stripe actually took, which is what a partial capture settles at.</summary>
    [JsonPropertyName("amount_received")]
    public long? AmountReceived { get; set; }

    [JsonPropertyName("amount_capturable")]
    public long? AmountCapturable { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("latest_charge")]
    public string? LatestCharge { get; set; }

    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
