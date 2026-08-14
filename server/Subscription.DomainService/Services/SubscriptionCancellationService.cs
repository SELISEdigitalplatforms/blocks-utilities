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
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = _contextResolver.Resolve(correlationId);

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

        var applied = immediately
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
            immediately,
            PaymentLogValue.Label(subscription.Status.ToString()),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(Reflect(subscription, immediately, now, reason)),
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
