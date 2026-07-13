using MongoDB.Bson.Serialization.Attributes;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsDeliveryAttempt
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string MessageId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public SmsProviderType ProviderType { get; set; }
    public string? ProviderMessageId { get; set; }
    public int AttemptNumber { get; set; }
    public SmsMessageStatus Status { get; set; } = SmsMessageStatus.Processing;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

