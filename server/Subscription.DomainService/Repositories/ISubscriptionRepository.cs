using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a subscription, returning false when the organization already has a live one.
    /// </summary>
    /// <remarks>
    /// The database decides, through a partial unique index, rather than a read followed by a
    /// write — two concurrent signups would both pass the read.
    /// </remarks>
    Task<bool> TryCreateAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken);

    /// <summary>Reads one subscription, scoped to the organization that owns it.</summary>
    Task<SubscriptionDetail?> GetAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>Reads without an organization filter, for background work that has no caller.</summary>
    Task<SubscriptionDetail?> GetByIdAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>The organization's subscription that currently grants something, if any.</summary>
    Task<SubscriptionDetail?> GetLiveAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken);

    Task<SubscriptionDetail?> GetByOrderIdAsync(
        string tenantId,
        string orderId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a subscription from one status to another, writing the transition's other fields
    /// and its event in the same update.
    /// </summary>
    /// <returns>
    /// False when the subscription is no longer in the expected status — someone else got there
    /// first, and the caller must not assume its own view is current.
    /// </returns>
    Task<bool> TryTransitionAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionTransition transition,
        CancellationToken cancellationToken);

    /// <summary>Changes a quantity item, guarded by the version the caller read.</summary>
    Task<bool> TryUpdateQuantityAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        string itemKey,
        long quantity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Swaps the plan, price and quantity snapshot, guarded by the version the caller read.
    /// </summary>
    /// <remarks>
    /// Guarded by <c>Version</c> rather than <c>Status</c>, unlike <see cref="TryTransitionAsync"/>:
    /// a plan change does not move the subscription's status, so there is no expected status to
    /// assert. The two are separate methods rather than one extended primitive because they
    /// write disjoint field sets for a reason — a status transition never touches the catalogue
    /// snapshot, and this never touches status.
    /// </remarks>
    /// <returns>False when the version has moved on — someone else changed this subscription first.</returns>
    Task<bool> TryChangePlanAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? planChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    /// <summary>Whether anything has ever subscribed to a plan, whatever became of it since.</summary>
    /// <remarks>
    /// Any status counts, cancelled included. Subscribing copies the plan's terms onto the
    /// subscription, and those terms are what its past invoices were computed from — so a plan
    /// that was ever sold has a history that editing the catalogue entry would silently
    /// contradict.
    /// </remarks>
    Task<bool> AnySubscriberAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken);

    /// <summary>Subscriptions whose first charge never completed, for the recovery sweep.</summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListStaleAsync(
        string tenantId,
        SubscriptionStatus status,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscriptions due for a renewal charge or a dunning retry — anything live whose next
    /// billing instant has arrived.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListDueForRenewalAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Subscriptions whose usage period has closed and needs its overage rated.</summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListDueForUsageRatingAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TryAppendEventAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    /// <summary>Takes a lease on one pending event so two workers cannot publish it twice.</summary>
    Task<SubscriptionOutboxEvent?> TryClaimEventAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDetail>> ListWithDueEventsAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken);

    Task MarkEventPublishedAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken);

    Task MarkEventFailedAsync(
        string tenantId,
        string subscriptionId,
        string eventId,
        string leaseId,
        SubscriptionOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string failureReason,
        CancellationToken cancellationToken);
}
