using Payment.DomainService.Enums;

namespace Payment.DomainService.Responses;

public sealed class ResponseMetadata
{
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public bool Replayed { get; init; }
}
