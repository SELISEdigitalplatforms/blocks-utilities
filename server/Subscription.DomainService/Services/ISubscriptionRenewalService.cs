using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>Charges one subscription's renewal, or its next dunning retry.</summary>
public interface ISubscriptionRenewalService
{
    Task RenewAsync(SubscriptionDetail subscription, CancellationToken cancellationToken);
}
