using System.Globalization;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>Stripe PaymentMethod, as returned by detach and retrieve.</summary>
public sealed class StripePaymentMethod
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Null once the method is detached, which is what detaching is for.</summary>
    [JsonPropertyName("customer")]
    public string? Customer { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("card")]
    public StripeCard? Card { get; set; }

    [JsonPropertyName("error")]
    public StripeError? Error { get; set; }
}

/// <summary>
/// The card details Stripe reports separately from the payment. A PaymentIntent event names
/// the payment method but carries none of this, so it has to be read back.
/// </summary>
public sealed class StripeCard
{
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last4")]
    public string? LastFour { get; set; }

    [JsonPropertyName("exp_month")]
    public int? ExpiryMonth { get; set; }

    [JsonPropertyName("exp_year")]
    public int? ExpiryYear { get; set; }

    [JsonPropertyName("funding")]
    public string? Funding { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Stripe reports expiry as numbers; the stored record keeps them as text.</summary>
    public string? ExpiryMonthText =>
        ExpiryMonth?.ToString("00", CultureInfo.InvariantCulture);

    public string? ExpiryYearText =>
        ExpiryYear?.ToString(CultureInfo.InvariantCulture);
}
