using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Stripe PaymentMethod, as returned by detach.</summary>
public sealed class StripePaymentMethod
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Null once the method is detached, which is what detaching is for.</summary>
    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}
