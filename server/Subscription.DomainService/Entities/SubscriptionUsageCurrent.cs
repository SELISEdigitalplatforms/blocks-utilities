using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The published state of one subscription, meter and usage period, shaped for reading.
/// </summary>
/// <remarks>
/// A projection of <see cref="SubscriptionUsageCounter"/>, published synchronously by the call that
/// moved the counter. It exists so a consumer can answer "how much is left?" with one indexed read
/// instead of resolving a subscription, walking its meters and point-reading a counter per meter.
/// <para>
/// <b>It is not an authority and must never be used as one.</b> Only
/// <c>POST /api/subscription-usage</c> with <c>enforce</c> can claim capacity, because only the
/// counter's atomic increment settles a race — two callers reading this document at the same instant
/// will both be told the same remaining figure, and they cannot both have it. Everything here is for
/// display and for cheap pre-checks that save a doomed request.
/// </para>
/// <para>
/// Derived, never computed. <see cref="Used"/>, <see cref="Remaining"/> and <see cref="Overage"/> are
/// copied from the counter result the authoritative write returned. Nothing increments this document:
/// an independent counter would be a second set of billing arithmetic, and the two would disagree
/// exactly when it mattered.
/// </para>
/// <para>
/// The identifier is composed the same way the counter's is, so a projection addresses its own source
/// without a lookup, crossing a period boundary simply addresses a different document, and the unique
/// index on subscription/meter/period is a restatement of the key rather than a second constraint
/// that could disagree with it.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class SubscriptionUsageCurrent
{
    /// <summary><c>{subscriptionId}:{meterKey}:{periodKey}</c> — the counter's own id.</summary>
    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// Empty for the organization's own aggregate row — the one every existing reader already
    /// expects, keyed by <see cref="CreateId(string, string, string)"/> exactly as before. Populated
    /// only on the additional per-user rows keyed by
    /// <see cref="CreateId(string, string, string, string)"/>, which track one acting user's own
    /// contribution to the same shared pool.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Which seat this row reports, or null when it reports the subscription as a whole.
    /// </summary>
    /// <remarks>
    /// A seated subscription counts each seat against its own window, so one row per subscription
    /// could only hold whichever seat published last — neither person's usage and not the total.
    /// <para>
    /// Null on every row written before seats existed and on every organization-wise subscription,
    /// which is what keeps those reading exactly as they always have.
    /// </para>
    /// </remarks>
    public int? SeatNumber { get; set; }

    /// <summary>
    /// The subscription's status when this was published, so a reader can tell a live allowance from
    /// one frozen by cancellation without joining to the subscription.
    /// </summary>
    public SubscriptionStatus SubscriptionStatus { get; set; }

    /// <summary>
    /// Whether the subscription is running out a scheduled cancellation, and the instant that
    /// cancellation stops entitlement.
    /// </summary>
    /// <remarks>
    /// <see cref="SubscriptionStatus"/> cannot answer this on its own: a subscriber who cancels
    /// keeps what they paid for, so the status stays <c>Active</c> right up to the boundary. A
    /// reader holding only the status therefore cannot tell a cancellation that has taken effect
    /// from one still running out its paid period — and revoking on the first sight of a
    /// cancellation takes access away on the day someone cancels, which this module does not do.
    /// <para>
    /// Absent on a row published before these fields existed, which reads as "no cancellation
    /// scheduled" and a null boundary — the same answer the status alone used to give.
    /// </para>
    /// </remarks>
    public bool CancelAtPeriodEnd { get; set; }

    /// <inheritdoc cref="CancelAtPeriodEnd"/>
    public DateTime? CurrentPeriodEndUtc { get; set; }

    public string PlanId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    /// <summary>
    /// How many decimal places this meter's quantities may carry, and so how far the balances on
    /// this document can be fractional. Zero means whole units only.
    /// </summary>
    /// <remarks>
    /// Carried here for the reader this collection exists for: one reading it directly over Mongo,
    /// with no API to ask. Such a reader meets <c>Used</c> of <c>512.5</c> with no way to tell
    /// whether that meter is measured to one place or to six — which it needs in order to format
    /// the figure, to decide what the next usable amount is, and to know that a step of one is
    /// wrong for it.
    /// <para>
    /// Terms rather than balance: it comes from the plan, so it moves with the subscription's
    /// version and never with the counter's. A document written before this field existed has none,
    /// deserializes to zero, and so reports whole units — which is what every meter was before
    /// fractional quantities existed.
    /// </para>
    /// </remarks>
    public int QuantityScale { get; set; }

    public string PeriodKey { get; set; } = string.Empty;

    public DateTime PeriodStartUtc { get; set; }

    /// <summary>
    /// <c>DateTime.MaxValue</c> for a never-reset capacity meter, whose window is the subscription's
    /// whole life. A boundary query for "the current period" therefore selects it correctly without
    /// naming it as a special case.
    /// </summary>
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>The allowance in force for this window, after any carry-forward.</summary>
    public decimal Included { get; set; }

    public decimal Used { get; set; }

    /// <summary>Never below zero. Copied from the authoritative result, not recomputed here.</summary>
    public decimal Remaining { get; set; }

    /// <summary>How far past the allowance this window has gone. Never below zero.</summary>
    public decimal Overage { get; set; }

    /// <summary>
    /// Whether the meter's terms permit going past <see cref="Included"/>. A reader with
    /// <see cref="Remaining"/> of zero needs this to know whether the next unit is refused or
    /// chargeable.
    /// </summary>
    public bool OverageAllowed { get; set; }

    /// <summary>
    /// The counter's <c>AppliedRecordCount</c> at the moment this was published.
    /// </summary>
    /// <remarks>
    /// The monotonic version this document is ordered by. It only ever rises on a given counter —
    /// <c>ApplyDeltaAsync</c> increments it by one per ledger entry and <c>TryRepairCounterAsync</c>
    /// only writes a value strictly greater than the one stored — which is what lets a conditional
    /// upsert reject a slow request carrying an older figure instead of letting it overwrite a newer
    /// one. Without it, two concurrent recordings would race to be last rather than to be highest.
    /// </remarks>
    public long CounterVersion { get; set; }

    /// <summary>
    /// How many ledger entries this row's <see cref="Used"/> reflects. Zero and unused on the
    /// organization's aggregate row, which is versioned by <see cref="CounterVersion"/> instead.
    /// </summary>
    /// <remarks>
    /// A per-user row has no atomic counter of its own to compare against — the aggregate's
    /// <see cref="CounterVersion"/> is the org-wide <c>SubscriptionUsageCounter.AppliedRecordCount</c>,
    /// which counts every user's entries together. This is the same idea narrowed to one user: the
    /// ledger's own count of that user's entries for this meter and period, which is what lets a
    /// repair tell a row that already reflects every entry from one that is missing some.
    /// </remarks>
    public long LedgerRecordCount { get; set; }

    /// <summary>
    /// <c>SubscriptionDetail.Version</c> at the moment this was published.
    /// </summary>
    /// <remarks>
    /// The second half of the ordering, and it is load-bearing rather than informational.
    /// <see cref="CounterVersion"/> moves only when usage is recorded, so a change that alters what
    /// this document <em>says</em> without altering the balance — a new plan, a changed quantity, a
    /// cancellation, a status transition — leaves the counter version exactly where it was. Ordering
    /// on the counter version alone, a republish carrying the new allowance would compare equal and
    /// be refused as stale, and the projection would keep advertising the old terms indefinitely.
    /// <para>
    /// Monotonic for the same reason the counter version is: every mutating write in
    /// <c>SubscriptionRepository</c> does <c>.Inc(subscription => subscription.Version, 1)</c>, so it
    /// only ever rises for a given subscription.
    /// </para>
    /// </remarks>
    public long SubscriptionVersion { get; set; }

    /// <summary>
    /// The shape of this document, so a consumer reading it directly can refuse an unfamiliar one
    /// rather than silently misread a field that changed meaning.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// When this derived document may be discarded, following the counter's own retention. The ledger
    /// behind it is kept regardless.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Raised to 2 by the addition of <see cref="QuantityScale"/>, to 3 by <see cref="UserId"/>,
    /// and to 4 by <see cref="CancelAtPeriodEnd"/> and <see cref="CurrentPeriodEndUtc"/>.
    /// </summary>
    /// <remarks>
    /// Raised rather than left alone because adding a field is invisible to both version
    /// comparisons: neither the counter's nor the subscription's version moves, so the
    /// reconciliation sweep could not otherwise tell that a stored document predates the field.
    /// Left at 1, a meter whose plan was authored before <see cref="QuantityScale"/> existed would
    /// report whole units for the life of its window — and a never-resetting meter's window does
    /// not end. The sweep treats a document below this as stale, so the ordinary cycle republishes
    /// it and no migration is needed.
    /// </remarks>
    public const int CurrentSchemaVersion = 4;

    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey) =>
        SubscriptionUsageCounter.CreateId(subscriptionId, meterKey, periodKey);

    /// <summary>
    /// The id of one user's row for this meter and period. Suffixed onto the aggregate's own id
    /// rather than composed independently, so the two can never collide by coincidence.
    /// </summary>
    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey,
        string userId) =>
        $"{CreateId(subscriptionId, meterKey, periodKey)}:{userId}";

    /// <summary>
    /// One seat's row, or the subscription's own when there is no seat.
    /// </summary>
    /// <remarks>
    /// Mirrors how the counter it projects is addressed, so a reader comparing the two is comparing
    /// the same window. A null seat composes the three-part identity every row already written
    /// uses, so nothing needs migrating.
    /// </remarks>
    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey,
        int? seatNumber) =>
        seatNumber is { } seat
            ? $"{CreateId(subscriptionId, meterKey, periodKey)}:s{seat}"
            : CreateId(subscriptionId, meterKey, periodKey);
}
