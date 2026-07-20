using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Entities;

public sealed class PaymentLifecycleEvent
{
    public int SchemaVersion { get; set; } = 1;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PaymentDetailId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? RefundId { get; set; }
    public decimal? RefundAmount { get; set; }
}
