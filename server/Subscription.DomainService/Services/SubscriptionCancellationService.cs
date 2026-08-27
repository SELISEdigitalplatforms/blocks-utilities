using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Ending a subscription.
/// </summary>
/// <remarks>
/// Cancelling ordinarily changes almost nothing today. The customer has paid through the end of
/// the period, so they keep what they bought and the subscription simply stops renewing —
/// taking access away on the day someone cancels is charging for a month and delivering part of
/// one.
/// <para>
/// When it was asked for and when it takes effect are separate fields because they are separate
/// facts, and support questions are usually about the gap between them.
/// </para>
/// </remarks>
public sealed class SubscriptionCancellationService : ISubscriptionCancellationService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionPaymentLinkRepository _links;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ILogger<SubscriptionCancellationService> _logger;
    private readonly TimeProvider _time;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly IUsagePeriodClosureRepository? _closures;

    public SubscriptionCancellationService(
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IEntitlementSnapshotCache cache,
        ILogger<SubscriptionCancellationService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null,
        IUsagePeriodClosureRepository? closures = null)
    {
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _scheduler = scheduler;
        _closures = closures;
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> CancelAsync(
        string subscriptionId,
        bool immediately,
        string? reason,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            organizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            // Another organization's subscription reports as missing rather than forbidden, so
            // a response cannot be used to confirm an identifier exists elsewhere.
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        if (subscription.Status is SubscriptionStatus.Canceled
            or SubscriptionStatus.IncompleteExpired)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_already_ended",
                "The subscription has already ended.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // A repeat request against a cancellation that is already scheduled. Status has not moved
        // — the subscription is still live — so this is not the "already ended" case above; it is
        // someone asking for the same thing (or an escalation this schedule cannot grant) twice.
        // Neither writes anything: a second CanceledAtUtc would silently move the request-time
        // timestamp on file, and a second event/version bump would be a lifecycle change nobody
        // actually made.
        if (subscription.CancelAtPeriodEnd)
        {
            if (!immediately || !subscription.CanCancelImmediately)
            {
                return SubscriptionOperationResult<SubscriptionResponse>.Success(
                    _mapper.ToResponse(subscription), correlationId);
            }

            // Escalating a schedule that is allowed to escalate. This is the one case a repeat
            // request against an already-scheduled cancellation still writes something.
            var escalated = await EndNowAsync(subscription, reason, now, correlationId, cancellationToken);

            if (!escalated)
            {
                return await ConvergeOrConflictAsync(
                    context, subscriptionId, wantsImmediate: true, correlationId, cancellationToken);
            }

            return await SucceedAsync(
                context, subscription, immediately: true, now, reason, correlationId, cancellationToken);
        }

        // An incomplete subscription has not bought a period to retain. Leaving it in
        // Incomplete with CancelAtPeriodEnd=true would reserve the organization forever:
        // there is no renewal boundary that can finish the cancellation. Treat abandoning
        // checkout as an immediate cancellation even when the caller uses the default flag.
        var endsImmediately = immediately || subscription.Status == SubscriptionStatus.Incomplete;

        // A year already paid for is a year the subscriber keeps. Ending access now would take the
        // money and the entitlement together, and this module refunds nothing — so an immediate
        // request is honoured as far as it can be: cancelled, no renewal, access to the end of the
        // term they bought. The subscriber loses nothing they paid for, which is the only reading
        // of "cancel now" that does not quietly become a forfeiture.
        //
        // Only for a *settled* year. One still owed is dropped by the ordinary path below without
        // charging for it. This is also the schedule's own CanCancelImmediately: an ordinary
        // cancellation may later be escalated; one already downgraded here for a prepaid year may
        // not be — escalating it a second time would be exactly the forfeiture this guarded against.
        var canCancelImmediately = subscription.PendingAnnualPeriod is not { IsPrepaid: true };

        if (endsImmediately && !canCancelImmediately)
        {
            endsImmediately = false;
        }

        // What EndAtPeriodEndAsync's own transition writes as CurrentPeriodEndUtc — computed here
        // too so the targeted work item below is scheduled against the same boundary that was
        // actually persisted, not recomputed later from a possibly-changed catalogue.
        var scheduledEffectiveAtUtc = subscription.PendingAnnualPeriod is { IsPrepaid: true } prepaid
            ? prepaid.EndUtc
            : subscription.CurrentPeriodEndUtc;

        var applied = endsImmediately
            ? await EndNowAsync(subscription, reason, now, correlationId, cancellationToken)
            : await EndAtPeriodEndAsync(
                subscription, reason, now, canCancelImmediately, correlationId, cancellationToken);

        if (!applied)
        {
            return await ConvergeOrConflictAsync(
                context, subscriptionId, endsImmediately, correlationId, cancellationToken);
        }

        if (!endsImmediately && _scheduler is not null)
        {
            // Best effort, and defensively so: this is a separate, non-transactional write from
            // the one just above that recorded the schedule. Its failure — even one the scheduler
            // itself failed to swallow — must not undo a cancellation the subscriber already has
            // confirmation of. The tenant repair sweep
            // (SubscriptionCancellationEffectiveProcessor.ProcessDueAsync) remains the durable path
            // regardless of whether this lands.
            try
            {
                await _scheduler.ScheduleCancellationEffectiveAsync(
                    subscription, scheduledEffectiveAtUtc, correlationId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Targeted cancellation work could not be scheduled and will be left to the " +
                    "repair sweep SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    correlationId);
            }
        }

        return await SucceedAsync(
            context, subscription, endsImmediately, now, reason, correlationId, cancellationToken,
            canCancelImmediately);
    }

    /// <summary>
    /// The bookkeeping every successful write shares: settle any pending checkout, drop the
    /// cached entitlement snapshot, log, and build the response from the in-memory reflection
    /// rather than a second read.
    /// </summary>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> SucceedAsync(
        SubscriptionContext context,
        SubscriptionDetail subscription,
        bool immediately,
        DateTime now,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken,
        bool canCancelImmediately = false)
    {
        await InvalidatePendingCheckoutAsync(subscription, cancellationToken);

        // The cached snapshot decides what the customer may do, so it has to go now rather
        // than in a few seconds' time.
        _cache.Invalidate(context.TenantId, context.OrganizationId);

        _logger.LogInformation(
            "Subscription cancellation recorded TenantHash={TenantHash} " +
            "OrganizationHash={OrganizationHash} SubscriptionHash={SubscriptionHash} " +
            "Immediate={Immediate} FromStatus={FromStatus} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            immediately,
            PaymentLogValue.Label(subscription.Status.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(Reflect(subscription, immediately, now, reason, canCancelImmediately)),
            correlationId);
    }

    /// <summary>
    /// What a lost compare-and-set means for a cancellation, specifically: another request may
    /// have already written the exact same outcome this one wanted, and re-reporting that as a
    /// conflict would be wrong — the caller's cancellation did happen, just not by this request's
    /// own write.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose: only the two shapes an idempotent duplicate could actually produce are
    /// treated as success. Anything else — a plan change, a quantity change, any other write that
    /// also moved this subscription in between — is a genuinely different state, and is reported
    /// as the conflict it is rather than guessed at.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ConvergeOrConflictAsync(
        SubscriptionContext context,
        string subscriptionId,
        bool wantsImmediate,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var latest = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        var converged = latest is not null && (wantsImmediate
            ? latest.Status == SubscriptionStatus.Canceled
            : latest.CancelAtPeriodEnd);

        if (converged)
        {
            return SubscriptionOperationResult<SubscriptionResponse>.Success(
                _mapper.ToResponse(latest!), correlationId);
        }

        return Failure(
            PaymentFailureKind.Conflict,
            "subscription_transition_conflict",
            "The subscription changed while it was being cancelled.",
            correlationId);
    }

    /// <summary>
    /// Closes the attempt that was still waiting on the provider, so a late confirmation cannot
    /// resurrect a subscription somebody has cancelled.
    /// </summary>
    /// <remarks>
    /// Belt and braces on top of the activation processor, which will not carry a subscription
    /// across unless it is still Incomplete. The difference matters for a card setup: the shopper
    /// may well complete the hosted page after cancelling — the tab is still open — and the
    /// provider will duly report a stored card. Settling the link here means the sweep stops
    /// looking, rather than returning to a subscription it can never act on until it runs out of
    /// attempts.
    /// <para>
    /// Only while the subscription had not started. Once it is live its link has been applied,
    /// and a renewal's link belongs to a charge this cancellation has nothing to say about.
    /// </para>
    /// </remarks>
    private async Task InvalidatePendingCheckoutAsync(
        SubscriptionDetail subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Status != SubscriptionStatus.Incomplete)
        {
            return;
        }

        var link = await _links.FindBySubscriptionAsync(
            subscription.TenantId,
            subscription.ItemId,
            cancellationToken);

        if (link is null || link.State != SubscriptionPaymentLinkState.Pending)
        {
            return;
        }

        await _links.TrySettleAsync(
            subscription.TenantId,
            link.ItemId,
            SubscriptionPaymentLinkState.Abandoned,
            cancellationToken);
    }

    /// <summary>
    /// The ordinary case: stop renewing, keep granting until the paid period ends.
    /// </summary>
    private async Task<bool> EndAtPeriodEndAsync(
        SubscriptionDetail subscription,
        string? reason,
        DateTime now,
        bool canCancelImmediately,
        string correlationId,
        CancellationToken cancellationToken) =>
        await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            // The status does not move: the customer paid through the end of the period and
            // keeps what they bought. Only the intention is recorded.
            new SubscriptionTransition(subscription.Status, subscription.Status)
            {
                CancelAtPeriodEnd = true,
                CanCancelImmediately = canCancelImmediately,
                CanceledAtUtc = now,
                CancellationReason = reason,
                ClearNextFeeBillingAt = true,
                // Status alone does not move here, so it cannot arbitrate two concurrent
                // first-time requests the way it does everywhere else — this is what does instead.
                RequireCancellationNotAlreadyScheduled = true,
                // A year already paid for is a year the subscriber keeps. Cancelling inside the
                // opening stub of a prepaid annual price therefore runs entitlement through to the
                // end of that year rather than stopping with the stub — they bought it, and this
                // module refunds nothing.
                CurrentPeriodEndUtc = subscription.PendingAnnualPeriod is { IsPrepaid: true } prepaid
                    ? prepaid.EndUtc
                    : null,
                // Either way the pending year stops being pending. Prepaid, it has just been folded
                // into the period above; unpaid, clearing the next billing instant above already
                // stopped its charge, and leaving the record behind would invite a later sweep to
                // find a year nobody is going to pay for.
                ClearPendingAnnualPeriod = subscription.PendingAnnualPeriod is not null,
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionCancellationRequested,
                    correlationId)
            },
            cancellationToken);

    private async Task<bool> EndNowAsync(
        SubscriptionDetail subscription,
        string? reason,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Stops new usage claims from being granted against this period before the transition
        // below even attempts to write — best effort like the escalation write itself is not
        // (see the try/catch there): closure failing here does not stop usage from eventually
        // being rated correctly, only from being refused quite as early as it ideally would be.
        if (_closures is not null)
        {
            var periodKey = PeriodKey.Create(
                subscription.UsageSchedule.Interval,
                subscription.CurrentUsagePeriodStartUtc);

            try
            {
                await _closures.StartClosingAsync(
                    subscription.TenantId,
                    subscription.ItemId,
                    periodKey,
                    now,
                    correlationId,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "The usage period could not be marked closing; a claim taken out in the " +
                    "next few moments could still be granted SubscriptionHash={SubscriptionHash} " +
                    "CorrelationId={CorrelationId}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    correlationId);
            }
        }

        return await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(subscription.Status, SubscriptionStatus.Canceled)
            {
                CancelAtPeriodEnd = false,
                // A cancellation that has already taken effect cannot be escalated again — this
                // flag is meaningless once Status is Canceled, and leaving the schedule's own
                // "true" behind would have the response advertise an escalation there is nothing
                // left to escalate.
                CanCancelImmediately = false,
                CanceledAtUtc = now,
                EndedAtUtc = now,
                CancellationReason = reason,
                ClearNextFeeBillingAt = true,
                // Entitlement stops now, so a year that had not started never will. Dropped so no
                // later sweep can find it and charge for a period this subscription never held.
                ClearPendingAnnualPeriod = subscription.PendingAnnualPeriod is not null,
                // Nothing more will be metered once entitlement stops immediately, so the usage
                // sweep should stop looking at this subscription's own clock — but the window
                // already open when it stopped still owes whatever overage it accrued. Queued
                // here rather than left to be forgotten, the same way a plan change detaches its
                // own outgoing window atomically with the schedule swap.
                ClearNextUsageBillingAt = true,
                OutgoingUsagePeriod = OutgoingUsagePeriodOf(subscription, now),
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionCanceled,
                    correlationId)
            },
            cancellationToken);
    }

    /// <summary>
    /// Freezes the subscription's current usage window exactly as a plan change freezes its own
    /// outgoing one, so the rating sweep can price it after status has already moved on.
    /// </summary>
    /// <remarks>
    /// Cut to <paramref name="effectiveAtUtc"/> rather than left at the window's own natural end:
    /// entitlement stopped there — whether this is a fresh immediate request or an escalated
    /// schedule — and an invoice that priced usage through the later, uncut end would be claiming
    /// to cover service the subscriber never actually had.
    /// </remarks>
    private static PendingUsagePeriod OutgoingUsagePeriodOf(
        SubscriptionDetail subscription,
        DateTime effectiveAtUtc) => new()
    {
        PeriodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc),
        PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
        PeriodEndUtc = effectiveAtUtc,
        Plan = subscription.Plan,
        Price = subscription.Price,
        CurrencyCode = subscription.CurrencyCode,
        CorrelationId = subscription.CorrelationId
    };

    /// <summary>
    /// Applies the transition to the copy being returned, so the caller sees what was written
    /// without a second read.
    /// </summary>
    private static SubscriptionDetail Reflect(
        SubscriptionDetail subscription,
        bool immediately,
        DateTime now,
        string? reason,
        bool canCancelImmediately = false)
    {
        subscription.CanceledAtUtc = now;
        subscription.CancellationReason = reason;
        subscription.NextFeeBillingAtUtc = null;

        // The pending year is settled either way, so the response must stop advertising one. A 200
        // still showing a year as pending would have a client offering to cancel something the
        // write it is reporting has already dealt with.
        if (subscription.PendingAnnualPeriod is { } pendingAnnual)
        {
            // Prepaid, entitlement now runs to the end of the year they bought; unpaid, the year is
            // simply dropped and the current period is the last one.
            if (!immediately && pendingAnnual.IsPrepaid)
            {
                subscription.CurrentPeriodEndUtc = pendingAnnual.EndUtc;
            }

            subscription.PendingAnnualPeriod = null;
        }

        if (immediately)
        {
            subscription.Status = SubscriptionStatus.Canceled;
            subscription.EndedAtUtc = now;
            // Escalating an existing schedule leaves these still set from before the write this
            // reflects — an effective cancellation cannot itself be cancelled, so both must clear
            // here exactly as they do in the transition EndNowAsync persists.
            subscription.CancelAtPeriodEnd = false;
            subscription.CanCancelImmediately = false;
        }
        else
        {
            subscription.CancelAtPeriodEnd = true;
            subscription.CanCancelImmediately = canCancelImmediately;
        }

        return subscription;
    }

    private static SubscriptionOperationResult<SubscriptionResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionResponse>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);
}
