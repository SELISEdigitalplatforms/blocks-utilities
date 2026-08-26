using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// One unit of payment background work, scheduled in the platform's root database.
/// </summary>
/// <remarks>
/// The root database is the scheduling layer and nothing more. Payments, captures and refunds stay
/// authoritative in their tenant databases — this document only says that something is due for a
/// tenant, so a worker can find it without walking a roster of thousands that have nothing to do.
/// <para>
/// A handler re-reads the tenant's own state before acting, because these two databases share no
/// transaction: a payment write can commit while the scheduling write is lost, and the reverse.
/// </para>
/// <para>
/// Carries no card details, no secrets, no provider payloads and no webhook bodies. It is queryable
/// across every tenant at once, which is exactly the property that makes storing any of those here
/// a bad idea — and payment payloads are the worst of them.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PaymentBackgroundWork
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string? OrganizationId { get; set; }

    /// <summary>
    /// What the work is about, when it is about one thing: a payment id, a refund id. Empty for
    /// tenant-wide work.
    /// </summary>
    public string AggregateId { get; set; } = string.Empty;

    public PaymentWorkType WorkType { get; set; }

    /// <summary>
    /// Which occurrence this is — a payment, a refund, a time bucket.
    /// </summary>
    /// <remarks>
    /// The half of the identity that makes producing idempotent. Two callers scheduling the same
    /// occurrence must land on one document, or the same payment gets two chances to be recovered
    /// and only the provider's idempotency stands between the customer and a second movement.
    /// </remarks>
    public string WorkKey { get; set; } = string.Empty;

    public BackgroundWorkStatus Status { get; set; } = BackgroundWorkStatus.Pending;

    public DateTime DueAtUtc { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>Lower runs first. Money that has moved outranks events about money.</summary>
    public int Priority { get; set; } = 100;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public string? LeaseId { get; set; }

    public string? LeasedBy { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? OperationId { get; set; }

    /// <summary>A classification, never a provider message.</summary>
    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// When a finished record may be removed. Set on completion only, which is what keeps the TTL
    /// index away from work that is unfinished or that somebody still has to look at.
    /// </summary>
    public DateTime? PurgeAtUtc { get; set; }
}
