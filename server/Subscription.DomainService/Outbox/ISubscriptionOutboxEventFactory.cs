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

    /// <summary>
    /// A cancellation event, told the boundary rather than left to read it off the subscription.
    /// </summary>
    /// <remarks>
    /// Every cancellation event is appended in the same compare-and-set that changes the state it
    /// announces, so the <paramref name="subscription"/> in hand is still the pre-write one: its
    /// own <c>CancelAtPeriodEnd</c> and <c>CurrentPeriodEndUtc</c> are what they were BEFORE this
    /// cancellation, and a payload built from them would tell a subscriber the opposite of what
    /// just happened. Passed explicitly for that reason, by the only caller that knows both.
    /// </remarks>
    /// <param name="effectiveAtUtc">
    /// When entitlement stops — the paid period's end for a schedule, the instant it ended for an
    /// immediate cancellation. Null for a withdrawal, which restores a subscription that is no
    /// longer stopping at all.
    /// </param>
    SubscriptionOutboxEvent CreateCancellation(
        SubscriptionDetail subscription,
        string eventType,
        bool cancelAtPeriodEnd,
        DateTime? effectiveAtUtc,
        string correlationId);

    /// <summary>A usage invoice's terminal outcome — charged, or abandoned after every retry.</summary>
    SubscriptionOutboxEvent CreateUsageRatingOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        string correlationId);
}
