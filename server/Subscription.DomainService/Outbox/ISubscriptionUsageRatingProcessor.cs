using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

/// <summary>Closes usage periods that have ended and invoices their overage.</summary>
public interface ISubscriptionUsageRatingProcessor
{
    /// <returns>How many usage periods were closed out, across every subscription swept.</returns>
    /// <summary>
    /// Closes every usage window that has ended, and reports which subscriptions actually rolled.
    /// </summary>
    /// <remarks>
    /// Returns the ids rather than a bare count because a point-of-change producer has to act on the
    /// subscriptions whose clocks <em>committed</em> a move, not on a second guess at which ones they
    /// were. Re-running the due query to find out is not equivalent: it has its own batch size, its
    /// own <c>now</c>, and by then the clocks have advanced — so it can name subscriptions that were
    /// not closed and miss ones that were.
    /// </remarks>
    Task<UsagePeriodClosureOutcome> CloseDuePeriodsAsync(
        string tenantId,
        CancellationToken cancellationToken);

    /// <returns>How many invoices were attempted, whether they charged, retried or were abandoned.</returns>
    Task<int> ChargeDueInvoicesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Closes exactly one subscription's own due periods, as of a caller-supplied instant —
    /// never the wall clock, and never any other subscription's schedule.
    /// </summary>
    /// <remarks>
    /// Exists for the simulation harness: passing the subscription's own current period end
    /// (or a moment after it) closes precisely that one period through the same logic
    /// <see cref="CloseDuePeriodsAsync"/> uses, without waiting for real time to reach it and
    /// without touching any other subscription the way a tenant-wide sweep would.
    /// </remarks>
    /// <returns>How many periods were closed for this one subscription.</returns>
    Task<int> CloseSubscriptionPeriodsAsync(
        SubscriptionDetail subscription,
        DateTime asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>Attempts exactly one invoice's charge, regardless of whether its own retry schedule is due.</summary>
    Task ChargeInvoiceAsync(SubscriptionUsageInvoice invoice, CancellationToken cancellationToken);
}

/// <summary>What one pass of usage-period closure committed.</summary>
/// <param name="PeriodsClosed">How many windows were closed, across every subscription.</param>
/// <param name="RolledSubscriptionIds">
/// The subscriptions that actually closed at least one window, and whose next window is therefore now
/// current. The authoritative answer to "who just rolled over".
/// </param>
public sealed record UsagePeriodClosureOutcome(
    int PeriodsClosed,
    IReadOnlyList<string> RolledSubscriptionIds)
{
    public static readonly UsagePeriodClosureOutcome Nothing = new(0, []);
}
