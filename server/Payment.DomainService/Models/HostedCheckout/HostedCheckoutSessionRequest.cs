using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class HostedCheckoutSessionRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; set; } = string.Empty;
    [JsonPropertyName("store")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Store { get; set; }
    [JsonPropertyName("amount")]
    public ProviderAmount Amount { get; set; } = new();
    [JsonPropertyName("returnUrl")]
    public string ReturnUrl { get; set; } = string.Empty;
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }
    [JsonPropertyName("themeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThemeId { get; set; }
    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;
    [JsonPropertyName("additionalData")]
    public ProviderAdditionalData AdditionalData { get; set; } = new();
    [JsonPropertyName("metadata")]
    public ProviderMetadata Metadata { get; set; } = new();
    [JsonPropertyName("storePaymentMethodMode")]
    public string StorePaymentMethodMode { get; set; } = "disabled";
    [JsonPropertyName("recurringProcessingModel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecurringProcessingModel { get; set; }
    [JsonPropertyName("shopperReference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopperReference { get; set; }
    [JsonPropertyName("shopperEmail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopperEmail { get; set; }
    [JsonPropertyName("shopperInteraction")]
    public string ShopperInteraction { get; set; } = "Ecommerce";
}
