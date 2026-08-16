using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface IUsageThresholdEvaluator
{
    /// <summary>
    /// Raises an event for any consumption threshold this increment has just crossed.
    /// </summary>
    /// <returns>How many thresholds were reported.</returns>
    Task<int> EvaluateAsync(
        SubscriptionDetail subscription,
        SubscriptionUsageCounter counter,
        string correlationId,
        CancellationToken cancellationToken);
}
