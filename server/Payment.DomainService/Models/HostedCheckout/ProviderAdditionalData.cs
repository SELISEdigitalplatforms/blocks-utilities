using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class ProviderAdditionalData
{
    /// <summary>
    /// Omitted when a capture delay is sent: Adyen refuses a session that carries both
    /// ("185 Invalid additionalData: submit captureDelayHours or manualCapture field, but not both").
    /// </summary>
    [JsonPropertyName("manualCapture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ManualCapture { get; set; }
}
