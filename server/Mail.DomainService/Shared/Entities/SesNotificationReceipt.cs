using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SesNotificationReceipt
{
    [BsonId]
    public string MessageId { get; set; } = string.Empty;
    public string MailItemId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = "Processing";
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
}
