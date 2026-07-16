using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class NotificationItem
{
    [JsonPropertyName("pspReference")]
    public string? PspReference { get; set; }
    [JsonPropertyName("originalReference")]
    public string? OriginalReference { get; set; }
    [JsonPropertyName("merchantAccountCode")]
    public string? MerchantAccountCode { get; set; }
    [JsonPropertyName("merchantReference")]
    public string? MerchantReference { get; set; }
    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; set; }
    [JsonPropertyName("amount")]
    public ProviderAmount? Amount { get; set; }
    [JsonPropertyName("eventCode")]
    public string? EventCode { get; set; }
    [JsonPropertyName("success")]
    public string? Success { get; set; }
    [JsonPropertyName("eventDate")]
    public DateTime? EventDate { get; set; }
    [JsonPropertyName("additionalData")]
    public Dictionary<string, string> AdditionalData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
