using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ILogger<SubscriptionCancellationService> _logger;
    private readonly TimeProvider _time;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly IUsagePeriodClosureRepository? _closures;
    private readonly IOptionsMonitor<SubscriptionOptions>? _options;
    private readonly ISubscriptionUsageRepository? _usage;
    private readonly IMeterAllowanceResolver? _allowances;
    private readonly ICampaignRedemptionRepository? _redemptions;

    public SubscriptionCancellationService(
        ISubscriptionRepository subscriptions,
        ISubscriptionPaymentLinkRepository links,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IBillingAccountRepository billingAccounts,
        IEntitlementSnapshotCache cache,
        ILogger<SubscriptionCancellationService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null,
        IUsagePeriodClosureRepository? closures = null,
        IOptionsMonitor<SubscriptionOptions>? options = null,
        ISubscriptionUsageRepository? usage = null,
        IMeterAllowanceResolver? allowances = null,
        ICampaignRedemptionRepository? redemptions = null)
    {
        _subscriptions = subscriptions;
        _links = links;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _billingAccounts = billingAccounts;
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _scheduler = scheduler;
        _closures = closures;
        _options = options;
        _usage = usage;
        _allowances = allowances;
        _redemptions = redemptions;
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
                    await _mapper.ToResponseAsync(
                        _billingAccounts, subscription, null, null, cancellationToken),
                    correlationId);
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
            await _mapper.ToResponseAsync(
                _billingAccounts,
                Reflect(subscription, immediately, now, reason, canCancelImmediately),
                null,
                null,
                cancellationToken),
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
                await _mapper.ToResponseAsync(
                    _billingAccounts, latest!, null, null, cancellationToken),
                correlationId);
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
        var closure = await ReserveClosureAsync(subscription, now, cancellationToken);

        if (closure is { Reserved: false })
        {
            // A different cancellation outcome already reserved this period — a genuinely
            // different boundary, not a retry of this one. Report no write of our own rather than
            // attempting a transition behind a reservation this call does not hold; the caller's
            // own convergence check decides whether the other outcome already satisfies this
            // request.
            return false;
        }

        var applied = await _subscriptions.TryTransitionAsync(
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
                OutgoingUsagePeriod = closure is { Reserved: true }
                    ? await OutgoingUsagePeriodOfAsync(subscription, now, cancellationToken)
                    : null,
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionCanceled,
                    correlationId)
            },
            cancellationToken);

        if (closure is { Reserved: true } reservation)
        {
            // Only reachable when _closures was non-null — that is the one thing ReserveClosureAsync
            // requires to return a non-null ClosureReservation at all.
            if (applied)
            {
                var outcome = await _closures!.TryCommitClosingAsync(
                    subscription.TenantId, subscription.ItemId, reservation.PeriodKey,
                    reservation.CloseOperationId, cancellationToken);

                if (outcome is not (ClosureCommitOutcome.Committed or ClosureCommitOutcome.AlreadyCommitted))
                {
                    // The transition just won, but the reservation could not be committed to
                    // Closing — left CloseReserved forever otherwise, blocking final invoicing.
                    // Not retried here: ReconcileStaleClosuresAsync's periodic sweep recovers any
                    // reservation left stuck like this, once it ages past its own timeout.
                    _logger.LogWarning(
                        "A usage closure reservation could not be committed after its " +
                        "cancellation won SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} " +
                        "Outcome={Outcome} CorrelationId={CorrelationId}",
                        PaymentLogValue.Hash(subscription.ItemId),
                        PaymentLogValue.Label(reservation.PeriodKey),
                        outcome,
                        correlationId);
                }
            }
            else
            {
                // Lost to an unrelated change (a plan change, a quantity change — not this same
                // cancellation, or ConvergeOrConflictAsync would have already been the one
                // deciding this). The period must not stay stuck refusing ordinary usage for a
                // cancellation that never actually happened.
                await _closures!.TryReleaseReservationAsync(
                    subscription.TenantId, subscription.ItemId, reservation.PeriodKey,
                    reservation.CloseOperationId, cancellationToken);
            }
        }

        // subscription.Status here is the status this call was asked to move it away from,
        // captured before the transition above -- Incomplete means it never activated. A
        // subscription cancelled after activating keeps its campaign redeemed; TryReleaseAsync's
        // own guard against an already-Redeemed row is defence in depth on top of that check, not
        // the only thing preventing it.
        if (applied &&
            _redemptions is not null &&
            subscription.Status == SubscriptionStatus.Incomplete &&
            subscription.Discount is { Campaign.Kind: not CampaignKind.Standard } discount)
        {
            await _redemptions.TryReleaseAsync(
                subscription.TenantId,
                discount.DiscountId!,
                subscription.ItemId,
                now,
                cancellationToken);
        }

        return applied;
    }

    /// <summary>
    /// Stakes this cancellation's claim on closing the subscription's current usage period, if it
    /// has one worth closing at all.
    /// </summary>
    /// <remarks>
    /// A storage failure is deliberately not caught here: proceeding with the subscription
    /// transition anyway is exactly the "cancellation succeeded but its usage period was never
    /// actually closed" gap this exists to prevent, so the caller must see the failure and refuse
    /// the request rather than silently press on.
    /// </remarks>
    private async Task<ClosureReservation?> ReserveClosureAsync(
        SubscriptionDetail subscription,
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken)
    {
        if (_closures is null || !CouldHaveAccruedUsage(subscription))
        {
            return null;
        }

        var periodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc);
        var closeOperationId =
            $"cancellation-close:{subscription.ItemId}:{effectiveAtUtc.Ticks}";

        var outcome = await _closures.TryReserveClosingAsync(
            subscription.TenantId,
            subscription.ItemId,
            periodKey,
            effectiveAtUtc,
            closeOperationId,
            cancellationToken);

        return new ClosureReservation(
            outcome == ClosureReservationOutcome.Reserved, periodKey, closeOperationId);
    }

    private sealed record ClosureReservation(bool Reserved, string PeriodKey, string CloseOperationId);

    /// <summary>
    /// Recovers reservations left <c>CloseReserved</c> longer than their timeout allows — the
    /// crash window between a cancellation's transition committing (or losing) in
    /// <see cref="EndNowAsync"/> or <c>SubscriptionCancellationEffectiveProcessor.TryFinalizeAsync</c>,
    /// and the commit-or-release call that was supposed to follow it ever actually landing.
    /// </summary>
    /// <remarks>
    /// Called directly from the tenant repair sweep (<c>SubscriptionRepairAnnouncer</c>) rather
    /// than only announced as queued work: reconciling a stuck closure state is not a financial
    /// side effect the way charging money or renewing a subscription is — nothing here moves
    /// money or changes what a subscriber was billed — so it does not need the sweep's own
    /// documented "never executes financial work directly" invariant, and a dedicated queue
    /// handler and work type would only add a hop for something this cheap to just do.
    /// <para>
    /// For each stale reservation, the subscription it belongs to is loaded and the reservation's
    /// own <see cref="UsagePeriodClosure.CloseOperationId"/> is checked against the one shape a
    /// genuine cancellation reservation can have for its own recorded boundary
    /// (<c>cancellation-close:{subscriptionId}:{effectiveEndUtcTicks}</c>) before anything is
    /// touched — a mismatch means something unexpected wrote this reservation, and guessing at it
    /// would risk closing or reopening a period for the wrong reason.
    /// </para>
    /// </remarks>
    public async Task<int> ReconcileStaleClosuresAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_closures is null)
        {
            return 0;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var options = _options?.CurrentValue ?? new SubscriptionOptions();
        var olderThanUtc = now.AddSeconds(-Math.Max(1, options.UsageClosureReservationTimeoutSeconds));

        var stale = await _closures.ListStaleReservationsAsync(
            tenantId, olderThanUtc, Math.Max(1, options.UsageClosureRecoveryBatchSize), cancellationToken);

        var reconciled = 0;

        foreach (var closure in stale)
        {
            if (await ReconcileOneAsync(closure, now, cancellationToken))
            {
                reconciled++;
            }
        }

        var claimCutoff = now.AddSeconds(-Math.Max(1, options.UsageClaimRecoveryTimeoutSeconds));
        var staleClaims = await _closures.ListStaleClaimsAsync(
                tenantId, claimCutoff, Math.Max(1, options.UsageClaimRecoveryBatchSize), cancellationToken)
            ?? [];

        foreach (var claim in staleClaims)
        {
            // ReleasePending is already owned by recovery. Active must first be claimed with a
            // conditional age/state transition so a request which made progress after this query
            // wins instead of being released underneath its write.
            if (claim.State == UsagePeriodClaimState.Active &&
                !await _closures.TryBeginStaleClaimRecoveryAsync(
                    tenantId, claim.ItemId, claimCutoff, cancellationToken))
            {
                continue;
            }

            await _closures.ReleaseClaimAsync(
                tenantId,
                claim.SubscriptionId,
                claim.PeriodKey,
                claim.IdempotencyKey,
                cancellationToken);
            reconciled++;
        }

        return reconciled;
    }

    private async Task<bool> ReconcileOneAsync(
        UsagePeriodClosure closure,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var expectedOperationId =
            closure.EffectiveEndUtc is { } effectiveEndUtc
                ? $"cancellation-close:{closure.SubscriptionId}:{effectiveEndUtc.Ticks}"
                : null;

        if (expectedOperationId is null || closure.CloseOperationId != expectedOperationId)
        {
            _logger.LogWarning(
                "A stale usage closure reservation does not have the shape a cancellation " +
                "reservation should — left untouched SubscriptionHash={SubscriptionHash} " +
                "PeriodKey={PeriodKey}",
                PaymentLogValue.Hash(closure.SubscriptionId),
                PaymentLogValue.Label(closure.PeriodKey));

            return false;
        }

        var boundary = closure.EffectiveEndUtc!.Value;

        var subscription = await _subscriptions.GetByIdAsync(
            closure.TenantId, closure.SubscriptionId, cancellationToken);

        if (subscription is null)
        {
            _logger.LogWarning(
                "A stale usage closure reservation names a subscription that no longer exists " +
                "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey}",
                PaymentLogValue.Hash(closure.SubscriptionId),
                PaymentLogValue.Label(closure.PeriodKey));

            return false;
        }

        var canceledAtBoundary =
            subscription.Status == SubscriptionStatus.Canceled && subscription.EndedAtUtc == boundary;
        var matchingScheduledBoundary =
            subscription.CancelAtPeriodEnd &&
            subscription.CurrentPeriodEndUtc == boundary &&
            now >= boundary;

        if (canceledAtBoundary || matchingScheduledBoundary)
        {
            var outcome = await _closures!.TryCommitClosingAsync(
                closure.TenantId, closure.SubscriptionId, closure.PeriodKey,
                closure.CloseOperationId!, cancellationToken);

            _logger.LogInformation(
                "Stale usage closure reservation reconciled by committing " +
                "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} Outcome={Outcome}",
                PaymentLogValue.Hash(closure.SubscriptionId),
                PaymentLogValue.Label(closure.PeriodKey),
                outcome);

            return outcome is ClosureCommitOutcome.Committed or ClosureCommitOutcome.AlreadyCommitted;
        }

        // A failed immediate escalation can leave its earlier boundary reserved while the
        // original period-end cancellation remains authoritative. Once that later boundary
        // arrives the subscription is no longer effectively live, so liveness alone cannot tell
        // us to release; the mismatching persisted boundary can and must.
        var abandonedEscalation =
            subscription.CancelAtPeriodEnd && subscription.CurrentPeriodEndUtc != boundary;

        if (abandonedEscalation || SubscriptionLiveness.IsEffectivelyLive(subscription, now))
        {
            var outcome = await _closures!.TryReleaseReservationAsync(
                closure.TenantId, closure.SubscriptionId, closure.PeriodKey,
                closure.CloseOperationId!, cancellationToken);

            _logger.LogInformation(
                "Stale usage closure reservation reconciled by releasing " +
                "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} Outcome={Outcome}",
                PaymentLogValue.Hash(closure.SubscriptionId),
                PaymentLogValue.Label(closure.PeriodKey),
                outcome);

            return outcome is ClosureReleaseOutcome.Released or ClosureReleaseOutcome.AlreadyReleased;
        }

        // Neither clearly ended at the reserved boundary nor clearly still live — ambiguous.
        // Left alone rather than guessed at; a human can inspect it, and it will be picked up
        // again by the next sweep pass regardless.
        _logger.LogWarning(
            "A stale usage closure reservation is ambiguous and was left untouched " +
            "SubscriptionHash={SubscriptionHash} PeriodKey={PeriodKey} SubscriptionStatus={Status}",
            PaymentLogValue.Hash(closure.SubscriptionId),
            PaymentLogValue.Label(closure.PeriodKey),
            PaymentLogValue.Label(subscription.Status.ToString()));

        return false;
    }

    /// <summary>
    /// Whether this subscription could ever have accrued billable usage at all. An abandoned
    /// <c>Incomplete</c> checkout never activated — nothing about it ever started a usage window —
    /// so there is nothing worth reserving, closing or rating for one.
    /// </summary>
    private static bool CouldHaveAccruedUsage(SubscriptionDetail subscription) =>
        subscription.Status != SubscriptionStatus.Incomplete;

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
    private async Task<PendingUsagePeriod> OutgoingUsagePeriodOfAsync(
        SubscriptionDetail subscription,
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken)
    {
        var periodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc);

        return new PendingUsagePeriod
        {
            PeriodKey = periodKey,
            PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
            PeriodEndUtc = effectiveAtUtc,
            Plan = subscription.Plan,
            Price = subscription.Price,
            CurrencyCode = subscription.CurrencyCode,
            CorrelationId = subscription.CorrelationId,
            // Snapshotted here, before the status transition below installs Canceled — the
            // subscription's trial and schedule are still the ones this window actually opened
            // under. Null (falling back to live resolution at rating time) when the
            // resolver/repository were not supplied.
            MeterAllowances = await MeterAllowanceSnapshot.CaptureAsync(
                subscription,
                new BillingPeriod(0, subscription.CurrentUsagePeriodStartUtc, effectiveAtUtc, periodKey),
                _usage,
                _allowances,
                cancellationToken)
        };
    }

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
