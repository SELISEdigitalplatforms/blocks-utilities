using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;

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
    public bool ProcessAsynchronously { get; set; }

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
    public HostedCheckoutSessionRequest? InitiationRequest { get; set; }
    public string? FrontendResultUrlSnapshot { get; set; }
    public string? ReturnStateNonceHash { get; set; }
    public string? ShopperReference { get; set; }
    public string? CheckoutSessionStatus { get; set; }
    public string? CheckoutResultCode { get; set; }
    public DateTime? CheckoutObservedAtUtc { get; set; }
    public string? SessionResultHash { get; set; }
    public DateTime? WebhookConfirmedAtUtc { get; set; }
    public PaymentInstrument? PaymentInstrument { get; set; }
    public List<PaymentOutboxEvent> OutboxEvents { get; set; } = [];
    public decimal RefundedAmount { get; set; }
    public decimal ReservedRefundAmount { get; set; }
    public List<PaymentRefund> Refunds { get; set; } = [];
}
