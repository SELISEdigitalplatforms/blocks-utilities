using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Whether a usage period may still accept writes that would change its billable balance.
/// </summary>
public enum UsagePeriodClosureState
{
    /// <summary>New usage claims are granted, subject to the boundary if one is set.</summary>
    Open = 0,

    /// <summary>
    /// A cancellation has staked a claim on closing this period but has not yet actually taken
    /// effect — its own subscription transition is still in flight. New usage claims are still
    /// granted here exactly as under <see cref="Open"/> (subject to the same boundary check): the
    /// cancellation this reservation belongs to might still lose its own compare-and-set and never
    /// happen, and a genuinely different, unrelated write must not be blocked by a reservation that
    /// turns out to have been for nothing.
    /// </summary>
    CloseReserved = 1,

    /// <summary>
    /// The cancellation that reserved this period committed. No new claim is granted. Rating must
    /// still wait for every claim taken out before this state was reached to be released.
    /// </summary>
    Closing = 2,

    /// <summary>Rated and invoiced. Terminal.</summary>
    Closed = 3
}

/// <summary>
/// One usage period's closure state — the record that decides whether a usage write may still
/// change a period's billable balance, independent of whatever the subscription document itself
/// currently says.
/// </summary>
/// <remarks>
/// Exists because the subscription's own liveness (<c>CancelAtPeriodEnd</c> /
/// <c>CurrentPeriodEndUtc</c>) is read independently by every usage request and by cancellation
/// finalization — two different reads of "now" that can disagree by however long two requests take
/// to reach their own writes. This document is the one thing both sides actually coordinate
/// through: a usage write must hold a claim against it before touching the ledger or the counter,
/// and finalization must see <see cref="ActiveWriterCount"/> reach zero before it rates the period,
/// so an invoice can never be generated while a usage operation that was already admitted has not
/// yet finished changing the balance it is about to be priced from.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class UsagePeriodClosure
{
    /// <summary><c>{subscriptionId}:{periodKey}</c> — one document per period, addressed directly.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public UsagePeriodClosureState State { get; set; } = UsagePeriodClosureState.Open;

    /// <summary>
    /// The instant entitlement actually stops, once a cancellation has reserved this period for
    /// closing. Null while <see cref="State"/> is <see cref="UsagePeriodClosureState.Open"/> — an
    /// ordinary period closes on its own schedule and never needs a claim rejected ahead of time.
    /// </summary>
    public DateTime? EffectiveEndUtc { get; set; }

    /// <summary>
    /// Usage claims taken out and not yet released. Rating must not proceed while this is above
    /// zero — the balance it would price is still being written.
    /// </summary>
    public int ActiveWriterCount { get; set; }

    /// <summary>
    /// Identifies which cancellation reserved, is closing, or closed this period — deterministic
    /// from the subscription and the boundary itself
    /// (<c>cancellation-close:{subscriptionId}:{effectiveAtUtcTicks}</c>), so two writers racing to
    /// finalize the same intended cancellation reserve the same operation rather than conflicting
    /// with each other, and only a genuinely different outcome (a different boundary) is refused.
    /// </summary>
    public string? CloseOperationId { get; set; }

    /// <summary>When the reservation was taken out, for a recovery sweep to age a stuck one by.</summary>
    public DateTime? ReservationCreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string CreateId(string subscriptionId, string periodKey) =>
        $"{subscriptionId}:{periodKey}";
}

public enum UsagePeriodClaimState
{
    Active = 0,
    Released = 1
}

/// <summary>
/// One usage request's hold against a period's closure state, for exactly as long as it takes to
/// write the ledger record and the counter delta it produces.
/// </summary>
/// <remarks>
/// Keyed by the request's own idempotency key so a retried request reuses its original claim
/// rather than taking out a second one — <see cref="UsagePeriodClosure.ActiveWriterCount"/> would
/// otherwise overcount, and never reach zero, for a period a client kept legitimately retrying
/// against.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class UsagePeriodClaim
{
    /// <summary><c>{subscriptionId}:{periodKey}:{idempotencyKey}</c>.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string PeriodKey { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public UsagePeriodClaimState State { get; set; } = UsagePeriodClaimState.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string CreateId(string subscriptionId, string periodKey, string idempotencyKey) =>
        $"{subscriptionId}:{periodKey}:{idempotencyKey}";
}
