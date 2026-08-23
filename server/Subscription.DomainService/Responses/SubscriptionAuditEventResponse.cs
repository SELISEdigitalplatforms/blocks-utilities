namespace Subscription.DomainService.Responses;

/// <summary>A safe operator-facing view of a subscription audit event.</summary>
public sealed class SubscriptionAuditEventResponse
{
    public string EventId { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public long? AmountMinor { get; init; }
    public string? CurrencyCode { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? FailureKind { get; init; }
    public int? Attempt { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}
