using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>Credit purchase records. See <see cref="SubscriptionCreditPurchase"/>.</summary>
public interface ISubscriptionCreditPurchaseRepository
{
    /// <summary>
    /// Records a purchase before it is charged. False when a purchase with the same id — the same
    /// subscription and idempotency key — already exists, in which case nothing was written.
    /// </summary>
    Task<bool> TryCreateAsync(SubscriptionCreditPurchase purchase, CancellationToken cancellationToken);

    Task<SubscriptionCreditPurchase?> GetAsync(
        string tenantId,
        string purchaseId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="purchase"/> over the stored one, only if the stored one is still at
    /// <paramref name="expectedVersion"/>. Raises the version on success.
    /// </summary>
    Task<bool> TryAdvanceAsync(
        SubscriptionCreditPurchase purchase,
        long expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>A subscription's purchases, newest first, optionally of one status.</summary>
    Task<IReadOnlyList<SubscriptionCreditPurchase>> ListAsync(
        string tenantId,
        string subscriptionId,
        CreditPurchaseStatus? status,
        CancellationToken cancellationToken);

    /// <summary>Purchases in one of <paramref name="statuses"/> last touched before <paramref name="olderThanUtc"/>.</summary>
    Task<IReadOnlyList<SubscriptionCreditPurchase>> ListStaleAsync(
        string tenantId,
        IReadOnlyCollection<CreditPurchaseStatus> statuses,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken);
}
