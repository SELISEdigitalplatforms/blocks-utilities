using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class TokenWebhookRequest
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}
