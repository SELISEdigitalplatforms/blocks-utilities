using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class NotificationContainer
{
    [JsonPropertyName("NotificationRequestItem")]
    public NotificationItem? Item { get; set; }
}
