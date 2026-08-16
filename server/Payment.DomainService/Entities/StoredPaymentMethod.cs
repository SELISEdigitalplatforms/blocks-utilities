using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class StoredPaymentMethod
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The organization a shopper's card listing is scoped to.
    /// </summary>
    /// <remarks>
    /// Visibility only: which caller this card is offered to. It is <b>not</b> necessarily the
    /// organization whose merchant account issued the token — see
    /// <see cref="EncryptionOrganizationId"/> for that. The two coincide when every organization
    /// is its own merchant, and diverge when organizations are subscribers of one tenant-level
    /// account, which is why they are two fields rather than one.
    /// </remarks>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The organization whose key ring protects <see cref="ProviderTokenCiphertext"/> — the
    /// resolved provider configuration's own organization, i.e. the merchant account that
    /// issued the token. Null means the tenant-level ring.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="OrganizationId"/>. A provider token is only usable
    /// at the merchant account that issued it, so encryption has to follow that account, not the
    /// caller who happened to save the card. Only meaningful when
    /// <see cref="EncryptionScopeResolvedAtUtc"/> is set — see
    /// <see cref="Utilities.PaymentEncryptionScope.From(StoredPaymentMethod)"/> for how a record
    /// written before this field existed still decrypts.
    /// </remarks>
    public string? EncryptionOrganizationId { get; set; }

    /// <summary>
    /// When <see cref="EncryptionOrganizationId"/> was resolved and recorded. Null on a record
    /// written before this distinction existed, which is the signal to fall back to
    /// <see cref="OrganizationId"/> instead — the only behaviour available at the time it was
    /// written, and still correct for a merchant-scoped organization.
    /// </summary>
    public DateTime? EncryptionScopeResolvedAtUtc { get; set; }
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
