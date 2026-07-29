using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentWebhookPayload
{
    public string? EventId { get; set; }
    public string? ProviderName { get; set; }
    public string? MerchantAccount { get; set; }
    public string? PaymentDetailId { get; set; }
    public string? RefundId { get; set; }
    public string? CaptureId { get; set; }
    public string? MerchantReference { get; set; }
    public string? PspReference { get; set; }
    public string? OriginalPspReference { get; set; }
    public bool? Success { get; set; }
    public long? AmountMinorUnits { get; set; }
    public string? CurrencyCode { get; set; }
    public string? ShopperReference { get; set; }

    /// <summary>
    /// The provider's own identifier for the payer, where it has one — Stripe's customer id.
    /// Needed to charge a saved card later, and not derivable from anything else we hold.
    /// Null for providers that address the shopper by our reference alone.
    /// </summary>
    public string? ProviderPayerReference { get; set; }
    public string? StoredPaymentMethodToken { get; set; }
    public string? PaymentMethodType { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public string? FundingSource { get; set; }
    public string? IssuerCountry { get; set; }
    public string? IssuerName { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? ProviderFailureCode { get; set; }
    public string? ProviderFailureSummary { get; set; }
    public string? ModificationAction { get; set; }
}
