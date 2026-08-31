using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A move onto another plan or price, waiting for the period the subscriber has already paid for
/// to run out.
/// </summary>
/// <remarks>
/// The plan-change counterpart of <see cref="PendingQuantityChange"/>, and held for the same
/// reason: a change that would cost nothing — or would come to less than what it replaces — is not
/// refunded, so it cannot take effect when it is asked for. The subscriber paid for the plan they
/// are on until <see cref="EffectiveAtUtc"/> and keeps it until then; entitlement stays truthful
/// for the rest of the period, and the renewal has something to act on when the boundary arrives.
/// <para>
/// One pending change at a time, replaced rather than queued — two downgrades in a period is a
/// customer changing their mind, not two instructions to carry out. A pending plan change also
/// blocks a quantity change and vice versa: both reprice the same period, and letting them
/// accumulate would leave two schedules disagreeing about what the next renewal is for.
/// </para>
/// <para>
/// Everything the boundary needs is frozen here, not re-resolved when it arrives. The plan and
/// price the subscriber was shown could be archived, repriced or have their discount changed in
/// the month between asking and applying, and a renewal that re-read the catalogue would move them
/// onto terms they never agreed to. The schedules are frozen for the same reason the amounts are:
/// they were derived from <see cref="EffectiveAtUtc"/>, so a worker running late still installs
/// the periods the change was quoted against rather than ones anchored on when it happened to run.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PendingPlanChange
{
    /// <summary>The plan being moved onto, exactly as it was when the change was accepted.</summary>
    public PlanSnapshot Plan { get; set; } = new();

    /// <summary>The price being moved onto, frozen for the same reason the plan is.</summary>
    public PriceSnapshot Price { get; set; } = new();

    /// <summary>
    /// The quantities to install alongside the plan. Carried explicitly rather than reusing the
    /// subscription's own: a plan change may name different quantity items than the plan being
    /// left defines, and the ones agreed to are the ones that apply.
    /// </summary>
    public List<SubscriptionQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>The fee schedule the target price bills on, derived from <see cref="EffectiveAtUtc"/>.</summary>
    public BillingSchedule FeeSchedule { get; set; } = new();

    /// <summary>
    /// The usage schedule the target plan meters on. Independent of the fee schedule, exactly as it
    /// is on a live subscription — an annual plan still meters monthly.
    /// </summary>
    public BillingSchedule UsageSchedule { get; set; } = new();

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
