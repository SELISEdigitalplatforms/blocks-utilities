using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Stripe Invoice, as returned by creation, finalization, payment and voiding — all four answer
/// with the invoice in its new state rather than an operation-specific object.
/// </summary>
public sealed class StripeInvoice
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary><c>draft</c>, <c>open</c>, <c>paid</c>, <c>uncollectible</c> or <c>void</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("amount_due")]
    public long? AmountDue { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>Present instead of the invoice fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
