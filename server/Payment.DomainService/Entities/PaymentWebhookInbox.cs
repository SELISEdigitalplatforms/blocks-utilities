using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentWebhookInbox
{
    [BsonId]
    public string WebhookId { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string WebhookType { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;

    /// <summary>
    /// What this event means, decided by the provider's normalizer at intake so the worker
    /// never has to interpret provider event names.
    /// </summary>
    public WebhookIntent Intent { get; set; } = WebhookIntent.Ignored;
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
