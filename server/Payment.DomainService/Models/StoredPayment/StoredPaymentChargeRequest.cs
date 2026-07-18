using System.Text.Json.Serialization;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Models.StoredPayment;

public sealed class StoredPaymentChargeRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public ProviderAmount Amount { get; init; } = new();

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;

    [JsonPropertyName("paymentMethod")]
    public StoredPaymentChargeMethod PaymentMethod { get; init; } = new();

    [JsonPropertyName("shopperReference")]
    public string ShopperReference { get; init; } = string.Empty;

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
