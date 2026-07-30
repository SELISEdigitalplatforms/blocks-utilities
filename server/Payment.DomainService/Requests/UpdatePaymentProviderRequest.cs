using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payment.DomainService.Requests;

/// <summary>
/// Replaces the editable, non-secret configuration of an existing payment
/// provider. Provider identity, endpoints, and credentials are intentionally
/// not part of this contract.
/// </summary>
public sealed class UpdatePaymentProviderRequest
{
    public long? Version { get; set; }

    public string FrontendResultUrl { get; set; } = string.Empty;

    public string? CountryCode { get; set; }

    public bool ManualCapture { get; set; }

    public int MaxRefundDays { get; set; }

    public string? StoreId { get; set; }

    public bool IsEnabled { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
}
