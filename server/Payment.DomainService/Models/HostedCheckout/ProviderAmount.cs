using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class ProviderAmount
{
    [JsonPropertyName("value")]
    public long Value { get; set; }
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;
}
