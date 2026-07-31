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
    public string? StoredPaymentMethodToken { get; set; }

    /// <summary>
    /// The provider's identifier for the payer that owns this method — Stripe's customer id.
    /// Required to charge the method off-session, and not derivable from the token. Null for
    /// providers that address the shopper by <see cref="ShopperReference"/> alone.
    /// </summary>
    public string? ProviderPayerReference { get; set; }
    public string? ProviderTokenCiphertext { get; set; }
    public string? ProviderTokenFingerprint { get; set; }

    /// <summary>
    /// The provider's stable identifier for the card itself, unchanged across every token it
    /// mints for that card. Identifies a card re-saved under a new token as the one already
    /// held, which the token fingerprint cannot: Stripe issues a fresh payment method on every
    /// checkout, so the same card saved twice otherwise appears as two. Null for providers
    /// whose token is already stable per card, such as Adyen.
    /// </summary>
    public string? ProviderCardFingerprint { get; set; }
    public string? TokenEncryptionKeyId { get; set; }
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
    public string? RemovalLeaseId { get; set; }
    public DateTime? RemovalLeaseExpiresAtUtc { get; set; }
    public DateTime? NextRemovalAttemptAtUtc { get; set; }
    public int RemovalAttemptCount { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public string? LastRemovalErrorCode { get; set; }
    public string? PaymentUseLeaseId { get; set; }
    public DateTime? PaymentUseLeaseExpiresAtUtc { get; set; }
}
