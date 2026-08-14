namespace Subscription.DomainService.Outbox;

public interface ISubscriptionActivationProcessor
{
    /// <summary>
    /// Carries confirmed payment outcomes into the subscriptions waiting on them.
    /// </summary>
    /// <returns>How many links were settled.</returns>
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);

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
