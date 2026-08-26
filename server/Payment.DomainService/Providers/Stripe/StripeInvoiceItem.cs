using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Stripe Invoice Item, as returned by <c>POST /v1/invoiceitems</c>.</summary>
public sealed class StripeInvoiceItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Present instead of the item fields when Stripe rejects the call.</summary>
    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
