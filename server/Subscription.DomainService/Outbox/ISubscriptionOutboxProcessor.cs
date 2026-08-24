using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

public interface ISubscriptionOutboxProcessor
{
    /// <summary>
    /// Publishes the events that have been appended to subscriptions but not yet sent.
    /// </summary>
    /// <returns>How many were published.</returns>
    Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes exactly one subscription's own due events, through the same claim-and-lease
    /// mechanism <see cref="PublishDueAsync"/> uses — for the simulation harness, which must
    /// never touch another subscription's outbox.
    /// </summary>
    /// <returns>How many events were published for this one subscription.</returns>
    Task<int> PublishDueForSubscriptionAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken);
}
