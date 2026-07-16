using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class HostedCheckoutPaymentMethod
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("brand")]
    public string? Brand { get; set; }
}
