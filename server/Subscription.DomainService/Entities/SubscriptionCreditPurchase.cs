using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One attempt to buy credits, recorded before the card is charged.
/// </summary>
/// <remarks>
/// Exists so a charge can never happen without a trace. A crash between the provider taking the
/// money and the grant being written would otherwise leave a paying customer with nothing, and no
/// record anywhere in this module that they paid. The recovery sweep finds such a record by its
/// <see cref="Status"/> and finishes the job from <see cref="OrderId"/>.
/// <para>
/// The identifier is composed from the subscription and the caller's idempotency key, so a retry
/// of the same request addresses the same document and a second charge has nowhere to be recorded.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionCreditPurchase
{
    /// <summary><c>{subscriptionId}:{idempotencyKey}</c>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// What was asked for, normalized. A retry with the same key must ask for exactly this, or it is
    /// a different purchase reusing a key and is refused.
    /// </summary>
    public string RequestFingerprint { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Null for the subscription's shared pool.</summary>
    public int? SeatNumber { get; set; }

    /// <summary>Whole credits.</summary>
    public decimal Quantity { get; set; }

    public string BuyerUserId { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = string.Empty;

    public long UnitAmountMinor { get; set; }

    /// <summary>Quantity times the unit price, before anything is taken off.</summary>
    public long GrossMinor { get; set; }

    public long DiscountMinor { get; set; }

    /// <summary>What was taxed.</summary>
    public long NetMinor { get; set; }

    public long TaxMinor { get; set; }

    /// <summary>What the card is charged.</summary>
    public long TotalMinor { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    /// <summary>The code as entered, normalized. Null when none was used.</summary>
    public string? DiscountCode { get; set; }

    /// <summary>The catalogue entry the code resolved to, and its version at the time.</summary>
    public string? DiscountId { get; set; }

    public long DiscountVersion { get; set; }

    /// <summary>The expiry policy the grant will carry, copied when quoted.</summary>
    public MeterResetPolicy ExpiryPolicy { get; set; }

    /// <summary>When the grant will expire, for one that expires with its window.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// The provider order id. Generated before charging so the sweep can ask about a charge whose
    /// response never came back.
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    public CreditPurchaseStatus Status { get; set; } = CreditPurchaseStatus.Pending;

    public string? PaymentId { get; set; }

    public string? GrantId { get; set; }

    public string? FailureCode { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; }

    public long Version { get; set; }

    public static string CreateId(string subscriptionId, string idempotencyKey) =>
        $"{subscriptionId}:{idempotencyKey}";
}
