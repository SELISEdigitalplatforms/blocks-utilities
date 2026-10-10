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

    /// <summary>Raised when a purchased quantity actually moves.</summary>
    /// <param name="previousQuantities">
    /// What the subscription held before the change. Passed rather than read off
    /// <paramref name="subscription"/>, because a renewal repaints its in-memory copy with the new
    /// quantities before pricing, and by then the copy no longer remembers the old ones.
    /// </param>
    /// <param name="quantities">What it holds once the write lands.</param>
    /// <param name="actorName">Who asked; for a change carried out later, who scheduled it.</param>
    SubscriptionOutboxEvent CreateQuantityChanged(
        SubscriptionDetail subscription,
        IReadOnlyList<SubscriptionQuantityItem> previousQuantities,
        IReadOnlyList<SubscriptionQuantityItem> quantities,
        string? actorName,
        string correlationId);

    /// <summary>
    /// A plan change. <paramref name="subscription"/>'s own <c>Plan.Code</c> must already be the
    /// new one — this only needs told what it changed <em>from</em>.
    /// </summary>
    SubscriptionOutboxEvent CreatePlanChanged(
        SubscriptionDetail subscription,
        string previousPlanCode,
        string? previousPlanName,
        string? actorName,
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
    /// <param name="reason">What the canceller gave as their reason, if anything.</param>
    /// <param name="actorName">Who asked; for a cancellation taking effect later, who requested it.</param>
    SubscriptionOutboxEvent CreateCancellation(
        SubscriptionDetail subscription,
        string eventType,
        bool cancelAtPeriodEnd,
        DateTime? effectiveAtUtc,
        string? reason,
        string? actorName,
        string correlationId);

    /// <summary>A person given or losing a seat, for the email that tells them.</summary>
    /// <param name="member">From IAM, or null when IAM could not say — the event still records the change.</param>
    SubscriptionOutboxEvent CreateMemberChanged(
        SubscriptionDetail subscription,
        string eventType,
        SubscriptionAssignment assignment,
        Services.MemberContact? member,
        string? organizationName,
        string? actorName,
        string correlationId);

    /// <summary>A usage invoice's terminal outcome — charged, or abandoned after every retry.</summary>
    SubscriptionOutboxEvent CreateUsageRatingOutcome(
        SubscriptionDetail subscription,
        string eventType,
        string periodKey,
        string correlationId);
}
