using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface IUsageThresholdEmailService
{
    Task SendAsync(
        SubscriptionLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken);
}
