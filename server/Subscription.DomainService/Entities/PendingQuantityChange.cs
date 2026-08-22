using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A reduction in purchased quantity, waiting for the period the subscriber has already paid for
/// to run out.
/// </summary>
/// <remarks>
/// A decrease is not refunded, so it cannot take effect when it is requested: the seats are paid
/// for until <see cref="EffectiveAtUtc"/> and the subscriber keeps them until then. Held here
/// rather than applied immediately so entitlement stays truthful for the rest of the period and
/// the renewal has something to act on.
/// <para>
/// One pending change at a time, replaced rather than queued. Two decreases in a period is a
/// customer changing their mind, not two instructions to carry out.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PendingQuantityChange
{
    public List<SubscriptionQuantityItem> RequestedQuantities { get; set; } = [];

    public DateTime RequestedAtUtc { get; set; }

    /// <summary>The end of the period already paid for, which is when this becomes real.</summary>
    public DateTime EffectiveAtUtc { get; set; }

    public string? RequestedByUserId { get; set; }

    /// <summary>
    /// The subscription version this was requested against, kept for the audit trail rather than
    /// for enforcement — the renewal applies whatever is pending when it runs.
    /// </summary>
    public int ExpectedVersion { get; set; }
}
