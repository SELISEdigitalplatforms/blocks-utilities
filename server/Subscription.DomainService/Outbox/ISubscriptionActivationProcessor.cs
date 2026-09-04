using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

public interface ISubscriptionActivationProcessor
{
    /// <summary>
    /// Carries confirmed payment outcomes into the subscriptions waiting on them.
    /// </summary>
    /// <returns>How many links were settled.</returns>
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Settles exactly one link, regardless of whether its own retry schedule says it is due yet.
    /// </summary>
    /// <remarks>
    /// Exists for callers that already hold one specific link and must not touch any other
    /// subscription's — the simulation harness, in particular, which would otherwise have to run
    /// the tenant-wide sweep and risk settling an unrelated link a test never asked about.
    /// </remarks>
    Task<bool> SettleLinkAsync(SubscriptionPaymentLink link, CancellationToken cancellationToken);

    /// <summary>
    /// Settles the links waiting on these specific payments, whatever their own retry schedule
    /// says.
    /// </summary>
    /// <remarks>
    /// The fast path for a confirmation that has just arrived. A webhook consumer holds both the
    /// confirmation and the subscription in one tick, and without this the link is only looked at
    /// when its deferred <c>NextCheckAtUtc</c> comes round — or, failing that, by the repair
    /// sweep two minutes later, which is where the paid-but-inactive window came from.
    ///
    /// Deliberately does nothing at all to a payment that is still undecided: no reschedule, no
    /// attempt burned, no audit row. Retry accounting belongs to the sweep alone, and a targeted
    /// pass that deferred would push the sweep's next look further out, making activation slower
    /// rather than faster.
    /// </remarks>
    /// <returns>How many links were settled.</returns>
    Task<int> SettleForPaymentsAsync(
        string tenantId,
        IReadOnlyCollection<string> paymentDetailIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds subscriptions whose first charge was raised but never recorded, and either
    /// recovers the link or gives up on them.
    /// </summary>
    /// <remarks>
    /// Covers the window between raising a charge and writing the link. Without it, a crash
    /// there leaves a subscription that took the customer's money and grants nothing, with
    /// nothing scanning for it.
    /// </remarks>
    Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken);
}
