using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentWebhookInbox
{
    [BsonId]
    public string WebhookId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string WebhookType { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? MerchantReference { get; set; }
    public DateTime EventDateUtc { get; set; }
    public PaymentWebhookPayload NormalizedPayload { get; set; } = new();
    public PaymentWebhookStatus Status { get; set; } = PaymentWebhookStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}
