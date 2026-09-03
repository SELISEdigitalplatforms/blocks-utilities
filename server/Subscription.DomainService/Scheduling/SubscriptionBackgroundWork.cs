using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// One unit of subscription background work, scheduled in the platform's root database.
/// </summary>
/// <remarks>
/// The root database is the scheduling layer and nothing more. Subscriptions, payments and usage
/// stay authoritative in their own tenant databases — this document only says that something is due
/// for a tenant, so a worker can find it without walking a roster of thousands of tenants that have
/// nothing to do.
/// <para>
/// Nothing here is a source of truth for money. A handler re-reads the tenant's own state before it
/// acts, because these two databases share no transaction: a tenant write can commit while the
/// scheduling write is lost, and the reverse. That is why the repair sweep stays.
/// </para>
/// <para>
/// Deliberately carries no card details, no secrets and no provider payloads. It is queryable by
/// operators across every tenant at once, which is exactly the property that makes storing any of
/// those here a bad idea.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionBackgroundWork
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string? OrganizationId { get; set; }

    /// <summary>
    /// What the work is about, when it is about one thing: a subscription id, a payment id. Empty
    /// for tenant-wide work, which is what the sweep schedules.
    /// </summary>
    public string AggregateId { get; set; } = string.Empty;

    public SubscriptionWorkType WorkType { get; set; }

    /// <summary>
    /// Which occurrence this is — a billing period, a usage window, a time bucket.
    /// </summary>
    /// <remarks>
    /// The half of the identity that makes producing idempotent. Two callers scheduling the same
    /// occurrence must land on one document, or a renewal gets two chances to charge and only the
    /// provider's own idempotency stands between the customer and a second debit.
    /// </remarks>
    public string WorkKey { get; set; } = string.Empty;

    public BackgroundWorkStatus Status { get; set; } = BackgroundWorkStatus.Pending;

    /// <summary>When the work became due. Kept for the "oldest due age" an operator watches.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>
    /// When it may next be claimed. Equal to <see cref="DueAtUtc"/> until an attempt fails, then
    /// pushed out by backoff.
    /// </summary>
    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>Lower runs first. Money-moving work outranks bookkeeping.</summary>
    public int Priority { get; set; } = 100;

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public string? LeaseId { get; set; }

    /// <summary>Which worker holds the lease, for tracing a stuck item to a pod.</summary>
    public string? LeasedBy { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Ties every log line for this work back to whatever asked for it.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// The W3C trace context of whatever scheduled this, when it was scheduled inside one.
    /// </summary>
    /// <remarks>
    /// An attempt uses this as a <strong>link</strong> rather than as a parent, deliberately. A
    /// renewal is scheduled a month before it runs and a cancellation up to a year; a span that made
    /// itself a child of a request which finished last November would describe a single trace as
    /// lasting a year — past every backend's retention window, and not a thing anybody can open.
    /// A link says the same causal thing without lying about duration.
    /// <para>
    /// Nullable, and left null by everything that schedules outside a request — the repair sweep
    /// among them, which mints its own correlation precisely because there is no caller to inherit
    /// from. Documents written before this field existed keep deserializing and keep meaning what
    /// they meant.
    /// </para>
    /// <para>
    /// A trace id is not a secret and names no person, so this is safe in the one collection
    /// operators query across every tenant at once. It is also the only thing here that could be
    /// mistaken for one, which is why it is spelled out.
    /// </para>
    /// </remarks>
    public string? TraceParent { get; set; }

    /// <summary>One attempt's own identity, so two attempts can be told apart in logs.</summary>
    public string? OperationId { get; set; }

    /// <summary>A classification, never a provider message: those carry data this must not hold.</summary>
    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// When a finished record may be removed. Set on completion only.
    /// </summary>
    /// <remarks>
    /// Left null while pending, processing or dead-lettered, which is what keeps the TTL index from
    /// deleting work that is unfinished or that somebody still has to look at.
    /// </remarks>
    public DateTime? PurgeAtUtc { get; set; }
}
