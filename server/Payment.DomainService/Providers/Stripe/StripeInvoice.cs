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

    /// <summary>What the invoice's own lines come to, before any customer balance is applied.</summary>
    [JsonPropertyName("total")]
    public long? Total { get; set; }

    /// <summary>
    /// The customer balance applied to this invoice. Positive is an amount the customer already
    /// owed and Stripe is collecting here; negative is credit reducing what is due.
    /// </summary>
    [JsonPropertyName("starting_balance")]
    public long? StartingBalance { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    /// <summary>
    /// Stripe's link to the rendered PDF. Unauthenticated and long-lived, so it is fetched when a
    /// document is asked for and never stored or handed to a caller.
    /// </summary>
    [JsonPropertyName("invoice_pdf")]
    public string? InvoicePdf { get; set; }

    /// <summary>Present instead of the invoice fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
