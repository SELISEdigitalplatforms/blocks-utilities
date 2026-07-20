using System.Text.Json.Serialization;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Models.Captures;

public sealed class ProviderCaptureRequest
{
    [JsonPropertyName("merchantAccount")]
    public string MerchantAccount { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public ProviderAmount Amount { get; init; } = new();

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}
