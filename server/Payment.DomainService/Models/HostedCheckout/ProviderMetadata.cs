using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

public sealed class ProviderMetadata
{
    [JsonPropertyName("value_a")]
    public string? TenantReference { get; set; }
    [JsonPropertyName("value_b")]
    public string? SiteId { get; set; }
    [JsonPropertyName("value_c")]
    public string? OrganizationId { get; set; }
}
