using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>An immutable, security-sensitive record of one subscription lifecycle step.</summary>
[BsonIgnoreExtraElements]
public sealed class SubscriptionAuditEvent
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string? SubscriptionId { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? UserId { get; set; }
    public string? PaymentDetailId { get; set; }
    public long? AmountMinor { get; set; }
    public string? CurrencyCode { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? FailureKind { get; set; }
    public int? Attempt { get; set; }
    public long? DurationMs { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
