using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Entities;

[BsonIgnoreExtraElements]
public sealed class PaymentCapture
{
    public string CaptureId { get; set; } =
        Guid.NewGuid().ToString();

    public string IdempotencyKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string Status { get; set; } =
        PaymentCaptureStatuses.Initiating;

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string ProviderReference { get; set; } = string.Empty;

    public string ProviderMerchantAccount { get; set; } =
        string.Empty;

    public string OriginalPaymentPspReference { get; set; } =
        string.Empty;

    public string? ProviderCaptureReference { get; set; }

    public string? ProviderResultStatus { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public string? ProcessingLeaseId { get; set; }

    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }

    public int InitiationAttemptCount { get; set; }

    public DateTime? NextRecoveryAttemptAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? LastProviderEventAtUtc { get; set; }
}
