using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class StoredPaymentMethod
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string ShopperReference { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string StoredPaymentMethodToken { get; set; } = string.Empty;
    public string Type { get; set; } = "scheme";
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? FundingSource { get; set; }
    public string? IssuerCountry { get; set; }
    public PaymentMethodStatus Status { get; set; } = PaymentMethodStatus.Active;
    public DateTime LastProviderEventAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextDeletionAttemptAtUtc { get; set; }
    public int DeletionAttemptCount { get; set; }
}
