using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.Refunds;

public sealed class ProviderReversalRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}
