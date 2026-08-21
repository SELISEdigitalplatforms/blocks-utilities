using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Resolves quantity increases that were reserved but never settled.
/// </summary>
/// <remarks>
/// The reservation exists so that a crash between charging and granting cannot lose either. This
/// is the half that acts on one: it asks the payment module what became of the charge and finishes
/// the job the original request could not.
/// <para>
/// The charge is findable at all because its idempotency key is derived from the reservation, and
/// that key is uniquely indexed — so deciding what happened is a point read, not a search.
/// </para>
/// </remarks>
public sealed class SubscriptionQuantityClaimProcessor : ISubscriptionQuantityClaimProcessor
{
    /// <summary>Payment statuses that mean the money is ours and the units are owed.</summary>
    private static readonly string[] ConfirmedStatuses =
    [
        PaymentStatuses.Authorized,
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyCaptured
    ];

    /// <summary>Payment statuses that will never become confirmed.</summary>
    private static readonly string[] TerminalFailureStatuses =
    [
        PaymentStatuses.Refused,
        PaymentStatuses.Cancelled,
        PaymentStatuses.MakePaymentFailed
    ];

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentRepository _payments;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionQuantityClaimProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionQuantityClaimProcessor(
        ISubscriptionRepository subscriptions,
        IPaymentRepository payments,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionQuantityClaimProcessor> logger,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _payments = payments;
        _events = events;
        _cache = cache;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = _time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddMinutes(-Math.Max(1, options.QuantityClaimGraceMinutes));

        var stale = await _subscriptions.ListStaleQuantityClaimsAsync(
            tenantId,
            cutoff,
            Math.Max(1, options.QuantityClaimBatchSize),
            cancellationToken);

        var resolved = 0;

        foreach (var subscription in stale)
        {
            if (subscription.QuantityChangeClaim is not { } claim)
            {
                continue;
            }

            if (await ResolveAsync(subscription, claim, cancellationToken))
            {
                resolved++;
            }
        }

        return resolved;
    }

    private async Task<bool> ResolveAsync(
        SubscriptionDetail subscription,
        QuantityChangeClaim claim,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.GetByIdempotencyKeyAsync(
            subscription.TenantId,
            SubscriptionConstants.QuantityChangeKeyFor(subscription.ItemId, claim.ClaimId),
            cancellationToken);

        if (payment is null)
        {
            // No charge was ever recorded under this reservation's key. Releasing is safe: the key
            // is derived, so if a charge did somehow reach the provider unrecorded, a later attempt
            // finds it rather than raising a second one.
            return await ReleaseAsync(subscription, claim, "no charge was recorded", cancellationToken);
        }

        if (TerminalFailureStatuses.Contains(payment.PaymentStatus))
        {
            return await ReleaseAsync(subscription, claim, "the charge failed", cancellationToken);
        }

        if (!ConfirmedStatuses.Contains(payment.PaymentStatus))
        {
            // Still in flight at the provider. Left alone rather than guessed at — the next pass
            // asks again, and guessing either way here is how a subscriber ends up charged for
            // units that were taken back.
            return false;
        }

        if (!await _subscriptions.TryPromoteQuantityClaimAsync(
                subscription.TenantId,
                subscription.ItemId,
                claim.ClaimId,
                claim.RequestedQuantities,
                claim.NewCreditBalanceMinor,
                payment.ItemId,
                _events.CreateQuantityChanged(subscription, claim.CorrelationId),
                cancellationToken))
        {
            // Someone else settled it between the read and the write, which is the outcome this
            // was trying to reach anyway.
            return false;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogWarning(
            "Granted a subscription quantity increase whose charge had settled unrecorded " +
            "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(claim.CorrelationId));

        return true;
    }

    private async Task<bool> ReleaseAsync(
        SubscriptionDetail subscription,
        QuantityChangeClaim claim,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!await _subscriptions.TryReleaseQuantityClaimAsync(
                subscription.TenantId,
                subscription.ItemId,
                claim.ClaimId,
                cancellationToken))
        {
            return false;
        }

        _logger.LogInformation(
            "Released an abandoned subscription quantity reservation because {Reason} " +
            "TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "CorrelationId={CorrelationId}",
            reason,
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(claim.CorrelationId));

        return true;
    }
}
