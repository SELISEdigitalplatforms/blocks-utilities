using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Requests;

/// <summary>
/// Rotates one or more provider credentials. Omitted credentials remain
/// unchanged. Webhook secrets retain the previous active value for overlap.
/// </summary>
public sealed class RotatePaymentProviderCredentialsRequest
{
    public long? Version { get; set; }

    public string? ApiKey { get; set; }

    public string? WebhookHmacKey { get; set; }

    public string? TokenHmacKey { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
}
