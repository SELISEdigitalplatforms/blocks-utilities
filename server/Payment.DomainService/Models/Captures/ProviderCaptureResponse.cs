using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.Captures;

public sealed class ProviderCaptureResponse
{
    [JsonPropertyName("pspReference")]
    public string? PspReference { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}
