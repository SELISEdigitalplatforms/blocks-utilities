namespace Subscription.DomainService.Outbox;

public interface ISubscriptionOutboxProcessor
{
    /// <summary>
    /// Publishes the events that have been appended to subscriptions but not yet sent.
    /// </summary>
    /// <returns>How many were published.</returns>
    Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken);
}
