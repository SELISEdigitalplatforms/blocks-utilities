using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface IEntitlementSnapshotCache
{
    Task<SubscriptionDetail?> GetAsync(
        string tenantId,
        string organizationId,
        Func<Task<SubscriptionDetail?>> loader);

    /// <summary>Drops an organization's cached subscription after it changes.</summary>
    void Invalidate(string tenantId, string organizationId);
}
