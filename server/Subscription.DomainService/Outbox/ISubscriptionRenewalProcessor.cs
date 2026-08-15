namespace Subscription.DomainService.Outbox;

/// <summary>Sweeps subscriptions due for a renewal charge or a dunning retry.</summary>
public interface ISubscriptionRenewalProcessor
{
    /// <returns>How many subscriptions were processed, whether they renewed or declined.</returns>
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);
}
