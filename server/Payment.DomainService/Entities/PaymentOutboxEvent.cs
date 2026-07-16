using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentOutboxEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string DeduplicationKey { get; set; } = string.Empty;
    public PaymentLifecycleEvent Payload { get; set; } = new();
    public PaymentOutboxStatus Status { get; set; } = PaymentOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public string? LeaseId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}
