using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Publishes the current-usage projection from authoritative state.
/// </summary>
/// <remarks>
/// The only writer of <see cref="SubscriptionUsageCurrent"/>. Every method here takes figures that a
/// counter has already produced; none of them adds, subtracts or carries anything forward, so the
/// projection cannot drift by arithmetic — only by not having been written yet, which is a condition
/// with a version on it and a repair behind it.
/// </remarks>
public interface IUsageProjectionPublisher
{
    /// <summary>
    /// Publishes one meter-period from the counter state a usage write ended at.
    /// </summary>
    /// <remarks>
    /// Called after the authoritative sequence has finished — ledger appended, counter moved, any
    /// reversal applied — so what it publishes is the balance the caller was told, never the momentary
    /// exceeded balance an enforced refusal passed through.
    /// </remarks>
    Task<UsageProjectionOutcome> PublishAsync(
        SubscriptionDetail subscription,
        PlanMeter meter,
        BillingPeriod period,
        SubscriptionUsageCounter counter,
        decimal allowance,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates zero-usage documents for every meter whose current window has none.
    /// </summary>
    /// <remarks>
    /// So a consumer can discover a subscription's meters and allowances before any usage exists.
    /// Insert-only per document: an existing balance is never overwritten.
    /// </remarks>
    Task<int> SeedCurrentAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Republishes every current window for a subscription from its counters.
    /// </summary>
    /// <remarks>
    /// The repair path, and the one every lifecycle change uses. Reads the authoritative counters and
    /// publishes what they say; the version condition means this cannot undo a recording that landed
    /// while it was running.
    /// <para>
    /// Also the correct response to anything that changes what the projection <em>describes</em>
    /// without changing a balance — a status change, a plan or quantity change, a repaired counter —
    /// because those alter the allowance or the terms a reader sees, not the usage.
    /// </para>
    /// </remarks>
    Task<int> RefreshAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>What became of one attempt to publish a projected document.</summary>
public enum UsageProjectionOutcome
{
    /// <summary>Written. The projection now describes the state the caller was told.</summary>
    Published = 0,

    /// <summary>
    /// A newer version was already stored, so nothing was written.
    /// </summary>
    /// <remarks>
    /// A success. It means a later recording against the same meter finished first, and the
    /// projection is ahead of this caller rather than behind it.
    /// </remarks>
    Superseded = 1,

    /// <summary>
    /// The usage committed but the projection could not be written, and a repair has been scheduled.
    /// </summary>
    /// <remarks>
    /// Reported to the caller as a diagnostic on an otherwise successful response, never as a failure:
    /// the counter is the authority, the usage is recorded, and a read model that could refuse a
    /// committed billing write would be an authority itself.
    /// </remarks>
    RepairScheduled = 2
}
