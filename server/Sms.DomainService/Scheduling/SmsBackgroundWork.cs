using MongoDB.Bson.Serialization.Attributes;

namespace Sms.DomainService.Scheduling;

public enum SmsWorkKind
{
    Retry = 1,
    DeliveryCheck = 2
}

public enum SmsWorkStatus
{
    Pending = 1,
    Leased = 2,
    Dead = 3
}

/// <summary>
/// One scheduled piece of SMS work, in the root database so the worker finds what is due without
/// walking every tenant. Holds ids only: no message text, numbers or credentials.
/// </summary>
/// <remarks>
/// Unique per (tenant, message, kind): scheduling again moves the due time of the existing item
/// rather than adding a second one. Completed items are deleted.
/// </remarks>
[BsonIgnoreExtraElements]
public class SmsBackgroundWork
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public SmsWorkKind Kind { get; set; }
    public SmsWorkStatus Status { get; set; } = SmsWorkStatus.Pending;
    public DateTime DueAtUtc { get; set; }
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Times the handler threw on this item. Not provider attempts; those live on the message.</summary>
    public int FailureCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
