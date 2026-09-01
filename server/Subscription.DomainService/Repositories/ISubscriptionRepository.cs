using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a subscription, returning false when the organization already has an open signup
    /// or a live subscription.
    /// </summary>
    /// <remarks>
    /// The database decides, through a partial unique reservation index that includes
    /// <see cref="SubscriptionStatus.Incomplete"/>, rather than a read followed by a write — two
    /// concurrent signups would both pass the read and could otherwise both reach checkout.
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

    /// <summary>
    /// The organization's subscription that currently grants something, if any.
    /// </summary>
    /// <remarks>
    /// A scheduled cancellation stops matching here the instant <c>nowUtc</c> passes its promised
    /// <c>CurrentPeriodEndUtc</c> — before the finalizing worker has necessarily run. Required
    /// rather than defaulted, so a caller cannot silently keep the older, worker-dependent
    /// behaviour by omission.
    /// </remarks>
    Task<SubscriptionDetail?> GetLiveAsync(
        string tenantId,
        string organizationId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>The organization's checkout that has not activated yet, if any.</summary>
    Task<SubscriptionDetail?> GetIncompleteAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The organization's subscription that lost access for want of a payment method, if any.
    /// </summary>
    /// <remarks>
    /// Not covered by <see cref="GetLiveAsync"/>, deliberately -- Unpaid grants nothing, which is
    /// the whole point of the status. This exists so a client can still be told a subscription is
    /// there and how to recover it, rather than reading the same "nothing" a tenant with no
    /// subscription at all would see.
    /// </remarks>
    Task<SubscriptionDetail?> GetUnpaidAsync(
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
    /// <param name="reservationId">
    /// The settlement reservation this write is promoting, or null for a change with no money to
    /// settle. When given, it addresses the write in place of the version: the charge has already
    /// been taken, so a concurrent version bump must not be able to strand paid-for terms.
    /// </param>
    /// <param name="replacementPendingAnnualPeriod">
    /// Given only by an opening-stub upgrade that settled a prepaid annual period alongside its
    /// stub: the new frozen annual figures to install in place of the old one. Left null for
    /// every other plan change, which leaves whatever annual period the subscription already
    /// carries untouched — an ordinary plan change has no annual period to touch, and passing null
    /// here must never be read as "clear it".
    /// </param>
    Task<bool> TryChangePlanAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        string? reservationId,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> newQuantityItems,
        SubscriptionPlanSchedule newSchedule,
        PendingUsagePeriod outgoingUsagePeriod,
        long newCreditBalanceMinor,
        string? planChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken,
        SubscriptionDocumentSource? documentSource = null,
        PendingAnnualPeriod? replacementPendingAnnualPeriod = null);

    /// <summary>
    /// Moves a subscription's purchased quantity, compare-and-set on the version.
    /// </summary>
    /// <remarks>
    /// Compare-and-set rather than a read-then-write so two administrators cannot both win: the
    /// loser is told to re-read rather than silently overwriting a seat count it never saw.
    /// </remarks>
    /// <param name="replacementPendingAnnualPeriod">
    /// Given only by an increase taken during a prepaid opening stub, which settles the stub and
    /// the annual period together at the new quantity: the new frozen annual figures to install.
    /// Null for every other quantity change, which leaves whatever annual period the subscription
    /// already carries untouched.
    /// </param>
    Task<bool> TryApplyQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? quantityChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken,
        SubscriptionDocumentSource? documentSource = null,
        PendingAnnualPeriod? replacementPendingAnnualPeriod = null);

    /// <summary>
    /// Reserves an increase before it is charged, compare-and-set on the version.
    /// </summary>
    /// <remarks>
    /// Refuses when a claim is already held, so one in-flight increase cannot be overtaken by a
    /// second quoted against the units the first has reserved.
    /// </remarks>
    Task<bool> TryReserveSettlementAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        SettlementReservation reservation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Grants the units a settled claim paid for, addressed by the claim rather than by a version.
    /// </summary>
    /// <remarks>
    /// Deliberately not compare-and-set on the version. The money has already moved; a concurrent
    /// change that happens to bump the version must not be able to strand units the subscriber has
    /// paid for. The claim id is the identity, and it is cleared by this write, so the promotion
    /// still happens exactly once.
    /// </remarks>
    Task<bool> TryPromoteQuantityReservationAsync(
        string tenantId,
        string subscriptionId,
        string reservationId,
        List<SubscriptionQuantityItem> newQuantityItems,
        long newCreditBalanceMinor,
        string? quantityChangePaymentDetailId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken,
        PendingAnnualPeriod? replacementPendingAnnualPeriod = null);

    /// <summary>Withdraws a claim whose charge never succeeded, leaving the quantity untouched.</summary>
    Task<bool> TryReleaseSettlementAsync(
        string tenantId,
        string subscriptionId,
        string reservationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscriptions holding a claim older than <paramref name="olderThanUtc"/> — an increase whose
    /// caller died between reserving and settling, which nothing else will ever finish.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListStaleSettlementsAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Schedules a decrease for the end of the paid period, replacing any already held.</summary>
    Task<bool> TrySetPendingQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        PendingQuantityChange pending,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a scheduled decrease.</summary>
    Task<bool> TryClearPendingQuantityChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Schedules a plan change for the end of the paid period, replacing any already held.
    /// </summary>
    /// <remarks>
    /// Refuses outright when a quantity change is already scheduled, rather than overwriting it:
    /// the two reprice the same period, and a caller that has just been shown a quote for one must
    /// not silently discard the other. The service checks the same thing first for a named error;
    /// this is the guarantee that holds when two callers race that check.
    /// </remarks>
    Task<bool> TrySetPendingPlanChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        PendingPlanChange pending,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a scheduled plan change.</summary>
    Task<bool> TryClearPendingPlanChangeAsync(
        string tenantId,
        string subscriptionId,
        int expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts one more card-collection attempt, so the next one is raised under a fresh key.
    /// </summary>
    /// <remarks>
    /// Compare-and-set on the attempt itself rather than the version: two tabs retrying at once
    /// must produce one increment, and the loser is told so rather than opening a third session
    /// under a number that is already taken.
    /// </remarks>
    Task<bool> TryBumpPaymentMethodSetupAttemptAsync(
        string tenantId,
        string subscriptionId,
        int expectedAttempt,
        CancellationToken cancellationToken);

    Task<bool> TryRemovePendingUsagePeriodAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
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

    /// <summary>
    /// Subscriptions with a scheduled period-end cancellation whose current period has run out —
    /// live in status, but past the instant they were told access would stop.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetail>> ListDueForCancellationAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TryAppendEventAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionOutboxEvent outboxEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records that a financial event owes a document, when no state change is carrying it.
    /// </summary>
    /// <remarks>
    /// The standalone form. A change that banks credit appends its source inside the same
    /// compare-and-set that banks it, because there the two must be atomic or the obligation is lost
    /// with nothing left to reconstruct it from; a settled charge has already committed by the time
    /// anything can be recorded, so it appends separately and keeps the payment as its backstop.
    /// <para>
    /// Filtered on no source already carrying the key, so a replayed transition appends once.
    /// </para>
    /// </remarks>
    /// <returns>False when one is already recorded, which is success as the caller means it.</returns>
    Task<bool> TryAppendDocumentSourceAsync(
        string tenantId,
        string subscriptionId,
        SubscriptionDocumentSource documentSource,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a source once its document exists.
    /// </summary>
    /// <remarks>
    /// Pulled rather than marked, so a healthy subscription carries none and the sweep's query stays
    /// a test for a non-empty array. Safe only after the document is inserted: the document is what
    /// makes a second attempt idempotent, so pulling first would turn a crash into a permanently
    /// missing document rather than a repeated one.
    /// </remarks>
    Task<bool> TryConsumeDocumentSourceAsync(
        string tenantId,
        string subscriptionId,
        string sourceKey,
        CancellationToken cancellationToken);

    /// <summary>Counts an attempt against a source that could not be issued, and says why.</summary>
    Task<bool> RecordDocumentSourceFailureAsync(
        string tenantId,
        string subscriptionId,
        string sourceKey,
        string errorCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscriptions whose trial began at or after an instant, oldest trial first.
    /// </summary>
    /// <remarks>
    /// The backstop for the one obligation that leaves no payment behind and can predate the record
    /// that would carry it: a trial that started before this module recorded obligations at all, or one
    /// whose record was lost. Walked forward from a stored mark rather than a fixed lookback, so a
    /// trial started during an outage of any length is still found.
    /// </remarks>
    /// <param name="afterId">
    /// The last subscription the previous page accounted for, or null to start inclusively. Trials
    /// begin in batches — a migration, a promotion — so several sharing one instant is ordinary.
    /// </param>
    Task<IReadOnlyList<SubscriptionDetail>> ListTrialsStartedSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        string? afterId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscriptions still owing a document.
    /// </summary>
    /// <remarks>
    /// Deliberately unbounded in time. A lookback window makes recovery a function of how long the
    /// worker was away, which is monitoring rather than recovery: an outage longer than the window
    /// leaves documents that are never issued and nothing that says so.
    /// </remarks>
    /// <param name="maximumAttempts">
    /// Sources that have failed this often are left out, so one document that can never be composed
    /// cannot starve every other subscription's.
    /// </param>
    Task<IReadOnlyList<SubscriptionDetail>> ListWithPendingDocumentSourcesAsync(
        string tenantId,
        int maximumAttempts,
        int limit,
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
