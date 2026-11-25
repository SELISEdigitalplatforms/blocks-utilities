using Subscription.DomainService.Scheduling;

namespace Subscription.DomainService.Services;

/// <summary>
/// Follows an accepted, applied charge until its money is captured or it fails, and hands a
/// failure to whoever can collect the amount again. Spec 009.
/// </summary>
public interface ISubscriptionChargeSettlementWatcher
{
    Task<SubscriptionWorkOutcome> CheckAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken);
}
