using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

public interface ISubscriptionOutboxEventFactory
{
    SubscriptionOutboxEvent Create(
        SubscriptionDetail subscription,
        string eventType,
        string correlationId,
        string? causationId = null);

    SubscriptionOutboxEvent CreateUsageThreshold(
        SubscriptionDetail subscription,
        SubscriptionUsageCounter counter,
        int thresholdPercent,
        string correlationId);
}
