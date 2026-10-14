using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface IUsageThresholdEvaluator
{
    /// <summary>
    /// Raises an event for any consumption threshold this increment has just crossed.
    /// </summary>
    /// <param name="liveCredits">
    /// Credits the window's recordings could still spend. With what the counter says credits have
    /// already paid for, it is added to the plan's allowance: thresholds are measured against
    /// everything the subscriber can use this window, so buying credits moves them out.
    /// </param>
    /// <returns>How many thresholds were reported.</returns>
    Task<int> EvaluateAsync(
        SubscriptionDetail subscription,
        SubscriptionUsageCounter counter,
        string correlationId,
        CancellationToken cancellationToken,
        decimal liveCredits = 0);
}
