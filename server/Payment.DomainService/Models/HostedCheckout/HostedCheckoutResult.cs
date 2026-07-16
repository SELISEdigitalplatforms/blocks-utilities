using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class HostedCheckoutResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("amount")]
    public ProviderAmount? Amount { get; set; }
    [JsonPropertyName("payments")]
    public List<HostedCheckoutPayment> Payments { get; set; } = [];
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}
