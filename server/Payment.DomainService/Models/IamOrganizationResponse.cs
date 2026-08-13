using System.Text.Json.Serialization;

namespace Payment.DomainService.Models;

/// <summary>
/// The slice of IAM's organization reply this service needs. Only the identifier is read:
/// an organization's name, tags and configuration are IAM's business, and copying them here
/// would create a second place they can go stale.
/// </summary>
public sealed class IamOrganizationResponse
{
    [JsonPropertyName("organization")]
    public IamOrganization? Organization { get; set; }
}

public sealed class IamOrganization
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }
}
