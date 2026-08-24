using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

/// <summary>Closes usage periods that have ended and invoices their overage.</summary>
public interface ISubscriptionUsageRatingProcessor
{
    /// <returns>How many usage periods were closed out, across every subscription swept.</returns>
    Task<int> CloseDuePeriodsAsync(string tenantId, CancellationToken cancellationToken);

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
