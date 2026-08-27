using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

/// <summary>Sweeps subscriptions whose scheduled period-end cancellation has come due.</summary>
public interface ISubscriptionCancellationEffectiveProcessor
{
    /// <returns>How many subscriptions were carried from scheduled to effective cancellation.</returns>
    Task<int> ProcessDueAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Finishes one subscription's scheduled cancellation, if it is still due. Shared by the
    /// tenant-wide sweep and a targeted work item naming this subscription specifically.
    /// </summary>
    /// <returns>False on a lost compare-and-set — another write already finished it.</returns>
    Task<bool> TryFinalizeAsync(SubscriptionDetail subscription, CancellationToken cancellationToken);
}
