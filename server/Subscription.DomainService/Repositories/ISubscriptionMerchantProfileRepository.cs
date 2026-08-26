using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>The tenant's own invoicing identity.</summary>
public interface ISubscriptionMerchantProfileRepository
{
    Task<SubscriptionMerchantProfile?> GetAsync(string tenantId, CancellationToken cancellationToken);

    Task<SubscriptionMerchantProfile> UpsertAsync(
        SubscriptionMerchantProfile profile,
        CancellationToken cancellationToken);
}
