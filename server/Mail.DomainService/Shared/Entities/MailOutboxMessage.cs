using Mail.DomainService.Shared.Enums;
using MongoDB.Bson.Serialization.Attributes;

namespace Mail.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class MailOutboxMessage
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string AggregateId { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string DeduplicationKey { get; set; } = string.Empty;
        public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
        public int AttemptCount { get; set; }
        public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAtUtc { get; set; }
        public string? LastError { get; set; }
        public string? ProjectKey { get; set; }
        public string? TenantId { get; set; }
        public string? OrganizationId { get; set; }
    }
}
