using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentDetail
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public string PaymentStatus { get; set; } = PaymentStatuses.Initiating;
    public DateTime ExpirationDate { get; set; }
    public double Amount { get; set; }
    public decimal PreciseAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? RequestId { get; set; }
    public string? TransactionId { get; set; }
    public DateTime PaymentDate { get; set; }
    public string? AcquirerName { get; set; }
    public string? AcquirerReference { get; set; }
    public string? SIXTransactionReference { get; set; }
    public string? ApprovalCode { get; set; }
    public string? PaymentMethod { get; set; }
    public bool RememberCard { get; set; }
    public bool UsesTransactionApi { get; set; }
    public bool IsRecurring { get; set; }
    public string? ClientSecret { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Whether this payment came from the console or from an application. See
    /// <see cref="PaymentOrigins"/>. Null on payments taken before this was recorded, which are
    /// not assumed to be either.
    /// </summary>
    public string? Origin { get; set; }

    /// <summary>
    /// The authenticated user who made the payment, so payments can be joined back to a user.
    /// Deliberately the id alone: name and email are not copied here, which keeps them out of
    /// the payments collection. Isolation between users does not rely on this field — that is
    /// enforced by <c>ShopperReference</c>, which is an HMAC of the tenant and the actor.
    /// </summary>
    public string? UserId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? MerchantId { get; set; }
    public string? CheckoutSessionId { get; set; }
    public string? ValidationId { get; set; }
    public string? BankTransactionId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhoneNumber { get; set; }
    public string? TerminalId { get; set; }
    public string? SessionId { get; set; }
    public string? SessionResult { get; set; }
    public string? SessionData { get; set; }
    public string? RedirectUrl { get; set; }
    public string? PspReference { get; set; }
    public string? CustomerOrganizationId { get; set; }
    public string? CaptureId { get; set; }
    public string? SiteId { get; set; }
    public string? OrderId { get; set; }
    public string? Description { get; set; }
    public bool ProcessAsynchronously { get; set; }
    public string PaymentFlow { get; set; } = PaymentFlows.HostedCheckout;
    public string? RecurringProcessingModel { get; set; }
    public string? StoredPaymentMethodPublicId { get; set; }
    public string? ProviderReference { get; set; }
    public string? ProviderMerchantAccount { get; set; }

    /// <summary>
    /// The provider's invoice behind this payment, when the money was collected through one.
    /// </summary>
    /// <remarks>
    /// Held so the invoice document can be fetched from the provider on demand rather than its
    /// download URL being stored: the URL is effectively a bearer token for the document, and one
    /// kept in the database outlives any decision to stop sharing it.
    /// </remarks>
    public string? ProviderInvoiceId { get; set; }

    /// <summary>Manual subscription-tax breakdown in minor units; null on older payments.</summary>
    public long? SubscriptionNetAmountMinor { get; set; }
    public long? SubscriptionTaxAmountMinor { get; set; }
    public long? SubscriptionCreditAmountMinor { get; set; }
    public int? SubscriptionTaxRateBasisPoints { get; set; }
    public string? SubscriptionTaxMode { get; set; }

    /// <summary>
    /// What this charge was made of before tax: the gross, what the price's own discount and the
    /// volume band took off between them, and what a promotional code took off after that. Null on
    /// payments raised before the breakdown was recorded, and on a first charge, which is a hosted
    /// checkout rather than an invoice this module composes.
    /// </summary>
    /// <remarks>
    /// All three recorded, not one combined figure. "Something came off" cannot be turned back into
    /// "the price gave 8% and the coupon gave nothing" — and which of the two it was is exactly what
    /// somebody reading an old invoice needs to know, by which time the catalogue has moved on.
    /// </remarks>
    public long? SubscriptionGrossAmountMinor { get; set; }
    public long? SubscriptionBuiltInDiscountMinor { get; set; }
    public long? SubscriptionPromotionalDiscountMinor { get; set; }

    /// <summary>The price's automatic rate, and how it met the volume band, as they stood when charged.</summary>
    public int? SubscriptionAutomaticDiscountBasisPoints { get; set; }
    public int? SubscriptionQuantityDiscountBasisPoints { get; set; }
    public string? SubscriptionDiscountCombination { get; set; }

    /// <summary>
    /// Set instead of the flat fields above when this payment settles a plan or quantity change,
    /// whose amount is a subtraction between two prorated periods rather than a discounted price.
    /// Null on a renewal, which the flat fields describe, and on anything raised before this existed.
    /// </summary>
    public SubscriptionSettlementBreakdown? SubscriptionSettlement { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public int? ProviderHttpStatus { get; set; }
    public string? ProcessingLeaseId { get; set; }
    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }
    public int InitiationAttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
    public ProviderInitiationRequest? InitiationRequest { get; set; }
    public string? FrontendResultUrlSnapshot { get; set; }
    public string? ReturnStateNonceHash { get; set; }
    public string? ShopperReference { get; set; }
    public string? CheckoutSessionStatus { get; set; }
    public string? CheckoutResultCode { get; set; }
    public DateTime? CheckoutObservedAtUtc { get; set; }
    public string? SessionResultHash { get; set; }
    public DateTime? WebhookConfirmedAtUtc { get; set; }
    public PaymentInstrument? PaymentInstrument { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal ReservedCaptureAmount { get; set; }
    public string CaptureStatus { get; set; } =
        PaymentCaptureStatuses.NotRequested;
    public string? CaptureMode { get; set; }
    public int? CaptureDelayHours { get; set; }
    public DateTime? LastCaptureEventAtUtc { get; set; }
    public List<PaymentCapture> Captures { get; set; } = [];
    public List<PaymentOutboxEvent> OutboxEvents { get; set; } = [];
    public decimal RefundedAmount { get; set; }
    public decimal ReservedRefundAmount { get; set; }
    public List<PaymentRefund> Refunds { get; set; } = [];
}
