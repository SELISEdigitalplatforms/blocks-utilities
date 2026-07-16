using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class StandardWebhookRequest
{
    [JsonPropertyName("notificationItems")]
    public List<NotificationContainer> NotificationItems { get; set; } = [];
}
