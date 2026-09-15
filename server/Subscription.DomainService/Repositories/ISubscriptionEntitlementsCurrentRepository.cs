using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The published entitlement-terms projection. See <see cref="SubscriptionEntitlementsCurrent"/>.
/// </summary>
public interface ISubscriptionEntitlementsCurrentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one subscription's entitlement terms, unless a newer version is already stored.
    /// </summary>
    /// <remarks>
    /// Conditional on <see cref="SubscriptionEntitlementsCurrent.SubscriptionVersion"/> being newer
    /// than what is stored, the same reasoning <c>ISubscriptionUsageCurrentRepository.TryPublishAsync</c>
    /// applies: the highest version wins rather than the last writer, so a refresh delayed behind a
    /// later plan change cannot overwrite it with older terms.
    /// </remarks>
    Task<bool> TryPublishAsync(
        SubscriptionEntitlementsCurrent document,
        CancellationToken cancellationToken);

    Task<SubscriptionEntitlementsCurrent?> GetAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken);
}
