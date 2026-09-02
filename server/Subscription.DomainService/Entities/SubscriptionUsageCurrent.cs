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
    /// The subscription's status when this was published, so a reader can tell a live allowance from
    /// one frozen by cancellation without joining to the subscription.
    /// </summary>
    public SubscriptionStatus SubscriptionStatus { get; set; }

    public string PlanId { get; set; } = string.Empty;

    public string PlanCode { get; set; } = string.Empty;

    public string MeterKey { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

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

    public const int CurrentSchemaVersion = 1;

    public static string CreateId(
        string subscriptionId,
        string meterKey,
        string periodKey) =>
        SubscriptionUsageCounter.CreateId(subscriptionId, meterKey, periodKey);
}
