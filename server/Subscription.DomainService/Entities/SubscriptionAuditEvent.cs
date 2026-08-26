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
    /// <summary>
    /// Why, in a person's words, when a person decided.
    /// </summary>
    /// <remarks>
    /// The field #274 asks an audit record to carry and the codebase had nowhere to put. Set for
    /// decisions somebody made — a dead letter requeued or set aside — and left null for outcomes
    /// the system reached on its own, where <see cref="ErrorCode"/> already says why.
    /// <para>
    /// Operator-supplied text, so it is never a place for a provider payload or anything read back
    /// out of one.
    /// </para>
    /// </remarks>
    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
