using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Stripe Checkout Session, as returned by create and retrieve.</summary>
public sealed class StripeCheckoutSession
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary><c>open</c>, <c>complete</c> or <c>expired</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary><c>unpaid</c>, <c>paid</c> or <c>no_payment_required</c>.</summary>
    [JsonPropertyName("payment_status")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("payment_intent")]
    public string? PaymentIntent { get; set; }

    [JsonPropertyName("amount_total")]
    public long? AmountTotal { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("client_reference_id")]
    public string? ClientReferenceId { get; set; }

    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }

    /// <summary>Present instead of the session fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}

/// <summary>Stripe's error envelope, returned as <c>{"error": {...}}</c>.</summary>
public sealed class StripeError
{
    /// <summary><c>card_error</c>, <c>invalid_request_error</c>, <c>api_error</c>, and so on.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("decline_code")]
    public string? DeclineCode { get; set; }

    [JsonPropertyName("param")]
    public string? Param { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// The intent the failed request created, when it created one.
    /// </summary>
    /// <remarks>
    /// A declined off-session confirmation still produces a PaymentIntent, and Stripe's later
    /// events name it. Keeping the id off the error means the payment holds no reference to
    /// the object those events are about, and they arrive unroutable.
    /// </remarks>
    [JsonPropertyName("payment_intent")]
    public StripeErrorPaymentIntent? PaymentIntent { get; set; }

    public string? PaymentIntentId => PaymentIntent?.Id;
}

public sealed class StripeErrorPaymentIntent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
