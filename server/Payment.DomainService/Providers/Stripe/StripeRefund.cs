using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Stripe Refund, as returned by <c>POST /v1/refunds</c>.</summary>
public sealed class StripeRefund
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// <c>pending</c>, <c>requires_action</c>, <c>succeeded</c>, <c>failed</c> or
    /// <c>canceled</c>. Card refunds usually return <c>succeeded</c> on the first call.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("payment_intent")]
    public string? PaymentIntent { get; set; }

    [JsonPropertyName("amount")]
    public long? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>Populated only when <see cref="Status"/> is <c>failed</c>.</summary>
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    /// <summary>Present instead of the refund fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
