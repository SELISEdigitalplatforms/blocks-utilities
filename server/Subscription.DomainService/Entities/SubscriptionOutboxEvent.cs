using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A domain event appended in the same write as the state change that caused it.
/// </summary>
/// <remarks>
/// Mongo and the message bus share no transaction, so publishing directly would lose events
/// whenever the write succeeded and the publish did not. Appending the event to the document
/// makes the two atomic, and a processor publishes afterwards.
/// <para>
/// <see cref="CorrelationId"/> and <see cref="CausationId"/> are persisted rather than passed,
/// because publication happens on another thread in another process: without them the trace
/// ends at the queue.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionOutboxEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Makes appending the event idempotent. The same transition attempted twice appends once.
    /// </summary>
    public string DeduplicationKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public SubscriptionOutboxStatus Status { get; set; } =
        SubscriptionOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public string? LeaseId { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>The request this event originated in, carried across the queue hop.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>What directly caused it, when that is not the originating request.</summary>
    public string? CausationId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }
}
