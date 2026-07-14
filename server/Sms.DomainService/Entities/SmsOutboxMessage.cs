using MongoDB.Bson.Serialization.Attributes;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsOutboxMessage
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string MessageId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public SmsOutboxStatus Status { get; set; } = SmsOutboxStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; } = 5;
    public DateTime NextVisibleAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastQueuedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
