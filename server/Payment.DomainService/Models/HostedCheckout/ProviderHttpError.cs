using System.Text.Json.Serialization;

namespace Payment.DomainService.Models.HostedCheckout;

internal sealed class ProviderHttpError
{
    [JsonPropertyName("status")]
    public int? Status { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("errorType")]
    public string? ErrorType { get; init; }
}
