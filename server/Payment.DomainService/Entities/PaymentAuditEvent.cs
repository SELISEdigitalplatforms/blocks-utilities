using MongoDB.Bson.Serialization.Attributes;

namespace Payment.DomainService.Entities;

/// <summary>
/// One decision somebody made about payments: a provider configured or its credentials rotated,
/// a payment, refund or capture requested, a saved card removed.
/// </summary>
/// <remarks>
/// Append-only, never updated or deleted, and with no TTL: a refund dispute or a question about who
/// rotated a merchant's credentials can come up long after the logs have aged out. Small on
/// purpose. It says who, what, when and how it ended; the logs, joined by
/// <see cref="CorrelationId"/>, say how. Never a card number, a token, a provider payload or a
/// secret.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PaymentAuditEvent
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The organization the subject belongs to; null for tenant-wide actions.</summary>
    public string? OrganizationId { get; set; }

    /// <summary><c>Payment</c>, <c>PaymentProvider</c>, <c>PaymentMethod</c> or <c>Tenant</c>.</summary>
    public string SubjectType { get; set; } = string.Empty;

    /// <summary>The payment, provider or payment-method id; null when the action failed before one existed.</summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// <c>PaymentCreate</c>, <c>RecurringPaymentCreate</c>, <c>PaymentRefund</c>, <c>PaymentCapture</c>,
    /// <c>PaymentProviderRegister</c>, <c>PaymentProviderUpdate</c>,
    /// <c>PaymentProviderCredentialRotate</c>, <c>PaymentSecretsReEncrypt</c>,
    /// <c>PaymentMethodRemove</c>.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary><c>Succeeded</c>, <c>Replayed</c>, <c>Pending</c> or <c>Rejected</c>.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Who acted, as the context resolver names them — <c>client:…</c> for an application.</summary>
    public string? ActorId { get; set; }

    public string? UserId { get; set; }

    /// <summary>Major units, as the payment API speaks them.</summary>
    public decimal? Amount { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>A short summary written by the code, never by a person — safe to show.</summary>
    public string? Detail { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
