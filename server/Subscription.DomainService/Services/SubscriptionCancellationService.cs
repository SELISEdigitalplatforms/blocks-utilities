using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
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
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ILogger<SubscriptionCancellationService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionCancellationService(
        ISubscriptionRepository subscriptions,
        ISubscriptionContextResolver contextResolver,
        ISubscriptionOutboxEventFactory events,
        ISubscriptionResponseMapper mapper,
        IEntitlementSnapshotCache cache,
        ILogger<SubscriptionCancellationService> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _contextResolver = contextResolver;
        _events = events;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
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
        // charging for it.
        if (endsImmediately && subscription.PendingAnnualPeriod is { IsPrepaid: true })
        {
            endsImmediately = false;
        }

        var applied = endsImmediately
            ? await EndNowAsync(subscription, reason, now, correlationId, cancellationToken)
            : await EndAtPeriodEndAsync(subscription, reason, now, correlationId, cancellationToken);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_transition_conflict",
                "The subscription changed while it was being cancelled.",
                correlationId);
        }

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
            endsImmediately,
            PaymentLogValue.Label(subscription.Status.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(Reflect(subscription, endsImmediately, now, reason)),
            correlationId);
    }

    /// <summary>
    /// The ordinary case: stop renewing, keep granting until the paid period ends.
    /// </summary>
    private async Task<bool> EndAtPeriodEndAsync(
        SubscriptionDetail subscription,
        string? reason,
        DateTime now,
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
                CanceledAtUtc = now,
                CancellationReason = reason,
                ClearNextFeeBillingAt = true,
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
        CancellationToken cancellationToken) =>
        await _subscriptions.TryTransitionAsync(
            subscription.TenantId,
            subscription.ItemId,
            new SubscriptionTransition(subscription.Status, SubscriptionStatus.Canceled)
            {
                CancelAtPeriodEnd = false,
                CanceledAtUtc = now,
                EndedAtUtc = now,
                CancellationReason = reason,
                ClearNextFeeBillingAt = true,
                // Entitlement stops now, so a year that had not started never will. Dropped so no
                // later sweep can find it and charge for a period this subscription never held.
                ClearPendingAnnualPeriod = subscription.PendingAnnualPeriod is not null,
                // Nothing more will be metered once entitlement stops immediately, so the usage
                // sweep should stop looking at this subscription too. Any usage already recorded
                // in the still-open final period goes unrated — a known, stated gap, not a
                // built recovery path.
                ClearNextUsageBillingAt = true,
                Event = _events.Create(
                    subscription,
                    SubscriptionConstants.SubscriptionCanceled,
                    correlationId)
            },
            cancellationToken);

    /// <summary>
    /// Applies the transition to the copy being returned, so the caller sees what was written
    /// without a second read.
    /// </summary>
    private static SubscriptionDetail Reflect(
        SubscriptionDetail subscription,
        bool immediately,
        DateTime now,
        string? reason)
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
        }
        else
        {
            subscription.CancelAtPeriodEnd = true;
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
