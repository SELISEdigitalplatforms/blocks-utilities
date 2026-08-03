using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class ProviderAdditionalData
{
    [JsonPropertyName("manualCapture")]
    public bool ManualCapture { get; set; }
}
