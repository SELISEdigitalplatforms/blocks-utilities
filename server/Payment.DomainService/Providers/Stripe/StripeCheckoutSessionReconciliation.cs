using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// A Checkout Session read with <c>payment_intent.payment_method</c> and
/// <c>setup_intent.payment_method</c> expanded, so a lost webhook's outcome can be reconstructed
/// from one call instead of guessed at from the bare session Standard reads use.
/// </summary>
/// <remarks>
/// Deliberately a separate model from <see cref="StripeCheckoutSession"/> rather than adding these
/// fields to it. That type's <c>PaymentIntent</c> is a plain id string everywhere else it is read,
/// and Stripe returns a full object in that same field once expansion is requested — modelling
/// both shapes on one property would make every existing caller's assumption silently wrong the
/// day this type's query parameters changed.
/// </remarks>
public sealed class StripeCheckoutSessionReconciliation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary><c>open</c>, <c>complete</c> or <c>expired</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary><c>unpaid</c>, <c>paid</c> or <c>no_payment_required</c>.</summary>
    [JsonPropertyName("payment_status")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("client_reference_id")]
    public string? ClientReferenceId { get; set; }

    [JsonPropertyName("payment_intent")]
    public StripeReconciliationIntent? PaymentIntent { get; set; }

    [JsonPropertyName("setup_intent")]
    public StripeReconciliationIntent? SetupIntent { get; set; }

    [JsonPropertyName("amount_total")]
    public long? AmountTotal { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>Present instead of the session fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}

/// <summary>A PaymentIntent or SetupIntent, expanded far enough to see what it stored.</summary>
public sealed class StripeReconciliationIntent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// PaymentIntent: <c>succeeded</c>, <c>requires_payment_method</c>, <c>canceled</c>, and so on.
    /// SetupIntent: <c>succeeded</c>, <c>requires_payment_method</c>, <c>canceled</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>The Stripe customer this intent is attached to, when one was named or created.</summary>
    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("payment_method")]
    public StripeReconciliationPaymentMethod? PaymentMethod { get; set; }
}

/// <summary>The saved card an intent's expanded <c>payment_method</c> carries.</summary>
public sealed class StripeReconciliationPaymentMethod
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("card")]
    public StripeReconciliationCard? Card { get; set; }
}

public sealed class StripeReconciliationCard
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last4")]
    public string? Last4 { get; set; }
}
