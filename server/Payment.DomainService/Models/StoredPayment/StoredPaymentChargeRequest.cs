using System.Text.Json.Serialization;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Models.StoredPayment;

public sealed class StoredPaymentChargeRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; init; } = string.Empty;

    /// <summary>
    /// The Adyen store the merchant account's payment methods are configured under, the same one
    /// the hosted checkout names.
    /// </summary>
    /// <remarks>
    /// Without it Adyen looks for an acquirer at merchant-account level only. An account set up per
    /// store has none there, so every off-session charge failed with <c>905_1</c> ("Could not find
    /// an acquirer account") while the shopper-present checkout for the same card succeeded.
    /// Omitted when the provider has no store, leaving that request body unchanged.
    /// </remarks>
    [JsonPropertyName("store")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Store { get; init; }

    [JsonPropertyName("amount")]
    public ProviderAmount Amount { get; init; } = new();

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;

    [JsonPropertyName("paymentMethod")]
    public StoredPaymentChargeMethod PaymentMethod { get; init; } = new();

    [JsonPropertyName("shopperReference")]
    public string ShopperReference { get; init; } = string.Empty;

    /// <summary>
    /// The provider's own identifier for the payer — Stripe's customer id.
    /// </summary>
    /// <remarks>
    /// Not the same thing as <see cref="ShopperReference"/>, which is ours and derived. Stripe
    /// will not charge a saved payment method without naming the customer it is attached to.
    /// Null for Adyen, which addresses the shopper by the derived reference alone, and excluded
    /// from serialisation so its Checkout API request body is unchanged.
    /// </remarks>
    [JsonIgnore]
    public string? ProviderPayerReference { get; init; }

    [JsonPropertyName("shopperInteraction")]
    public string ShopperInteraction { get; init; } = "ContAuth";

    [JsonPropertyName("recurringProcessingModel")]
    public string RecurringProcessingModel { get; init; } = string.Empty;

    [JsonPropertyName("shopperStatement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopperStatement { get; init; }

    [JsonPropertyName("metadata")]
    public ProviderMetadata Metadata { get; init; } = new();
}
