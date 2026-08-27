namespace Subscription.DomainService.Outbox;

/// <summary>Sweeps subscriptions whose scheduled period-end cancellation has come due.</summary>
public interface ISubscriptionCancellationEffectiveProcessor
{
    /// <returns>How many subscriptions were carried from scheduled to effective cancellation.</returns>
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);
}
