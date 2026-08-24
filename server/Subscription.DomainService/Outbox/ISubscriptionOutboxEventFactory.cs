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

    /// <summary>A renewal or dunning attempt's outcome, scoped to the period it charged.</summary>
    SubscriptionOutboxEvent CreateRenewalOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        int attemptNumber,
        string correlationId);

    /// <summary>
    /// A plan change. <paramref name="subscription"/>'s own <c>Plan.Code</c> must already be the
    /// new one — this only needs told what it changed <em>from</em>.
    /// </summary>
    /// <summary>Raised when a purchased quantity actually moves.</summary>
    SubscriptionOutboxEvent CreateQuantityChanged(
        SubscriptionDetail subscription,
        string correlationId);

    SubscriptionOutboxEvent CreatePlanChanged(
        SubscriptionDetail subscription,
        string previousPlanCode,
        string correlationId);

    /// <summary>A usage invoice's terminal outcome — charged, or abandoned after every retry.</summary>
    SubscriptionOutboxEvent CreateUsageRatingOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        string correlationId);
}
