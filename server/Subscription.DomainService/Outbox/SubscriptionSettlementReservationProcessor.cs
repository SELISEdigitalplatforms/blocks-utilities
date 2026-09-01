using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Resolves quantity increases that were reserved but never settled.
/// </summary>
/// <remarks>
/// The reservation exists so that a crash between charging and granting cannot lose either. This is
/// the half that acts on one: it establishes what the provider actually did and finishes the job
/// the original request could not.
/// <para>
/// Absence of a payment record is never taken as proof that no money moved — a request that timed
/// out may have been collected and never answered. Where the record is missing, the charge is
/// replayed under the reservation's own idempotency key, which either returns the charge already
/// raised or raises the one that never was. Only an answer that states the money did not move
/// releases the reservation.
/// </para>
/// </remarks>
public sealed class SubscriptionSettlementReservationProcessor : ISubscriptionSettlementReservationProcessor
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

    /// <summary>
    /// Failures that state plainly that no money moved, and so may release the reservation.
    /// </summary>
    /// <remarks>
    /// Everything absent from this list is an unanswered charge rather than a declined one, and is
    /// left held for the next pass. A reservation is cheap to keep and impossible to undo.
    /// </remarks>
    private static readonly PaymentFailureKind[] SettledFailureKinds =
    [
        PaymentFailureKind.ProviderRejected,
        PaymentFailureKind.Validation,
        PaymentFailureKind.NotFound,
        PaymentFailureKind.Conflict,
        PaymentFailureKind.RateLimited
    ];

    /// <summary>
    /// How many grace windows a reservation may go unresolved before it is worth waking somebody.
    /// </summary>
    /// <remarks>
    /// A reservation this old blocks the subscriber's quantity and plan changes, and nothing here
    /// can safely clear it: releasing might discard a charge, promoting might grant units nobody
    /// paid for. That is a person's decision, so it is logged as one.
    /// </remarks>
    private const int GraceWindowsBeforeAlerting = 8;

    private readonly ISubscriptionRepository _subscriptions;
    private readonly IPaymentRepository _payments;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionSettlementReservationProcessor> _logger;
    private readonly TimeProvider _time;

    public SubscriptionSettlementReservationProcessor(
        ISubscriptionRepository subscriptions,
        IPaymentRepository payments,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IOptionsMonitor<SubscriptionOptions> options,
        ILogger<SubscriptionSettlementReservationProcessor> logger,
        TimeProvider? time = null,
        ISubscriptionFinancialDocumentAnnouncer? documents = null)
    {
        _subscriptions = subscriptions;
        _payments = payments;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _documents = documents;
    }

    /// <summary>
    /// Announces the invoice for a settlement this sweep recovered.
    /// </summary>
    /// <remarks>
    /// Needed here as well as in the request path, and for the same reason this whole class exists:
    /// the caller that charged the card may never have come back. Its invoice would otherwise be left
    /// to the document sweep's own lookback window, which is shorter than the reservation grace.
    /// </remarks>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _documents;

    public async Task<int> RecoverStaleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var graceMinutes = Math.Max(1, options.SettlementReservationGraceMinutes);
        var now = _time.GetUtcNow().UtcDateTime;

        var stale = await _subscriptions.ListStaleSettlementsAsync(
            tenantId,
            now.AddMinutes(-graceMinutes),
            Math.Max(1, options.SettlementReservationBatchSize),
            cancellationToken);

        var resolved = 0;

        foreach (var subscription in stale)
        {
            if (subscription.SettlementReservation is not { } reservation)
            {
                continue;
            }

            if (await ResolveAsync(subscription, reservation, cancellationToken))
            {
                resolved++;

                continue;
            }

            WarnIfLongUnresolved(subscription, reservation, now, graceMinutes);
        }

        return resolved;
    }

    private async Task<bool> ResolveAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        CancellationToken cancellationToken)
    {
        var payment = await FindChargeAsync(subscription, reservation, cancellationToken);

        if (payment is null)
        {
            // No record, which is not the same as no charge. Ask the provider by replaying under
            // the reservation's key rather than assuming.
            return await ReplayAsync(subscription, reservation, cancellationToken);
        }

        if (TerminalFailureStatuses.Contains(payment.PaymentStatus))
        {
            return await ReleaseAsync(subscription, reservation, "the charge failed", cancellationToken);
        }

        if (!ConfirmedStatuses.Contains(payment.PaymentStatus))
        {
            // Still in flight at the provider. Left alone rather than guessed at — guessing either
            // way here is how a subscriber ends up charged for units that were taken back.
            return false;
        }

        return await PromoteAsync(subscription, reservation, payment.ItemId, cancellationToken);
    }

    /// <summary>
    /// Re-raises the reservation's charge under its own idempotency key and routing.
    /// </summary>
    /// <remarks>
    /// Safe precisely because the key is derived from the reservation: if the provider already
    /// collected, it answers with that same charge rather than a second one. This is the only way to
    /// tell "never charged" from "charged and the answer was lost", and the difference decides
    /// whether the subscriber keeps their money or their units.
    /// <para>
    /// Routed from the reservation, never from the billing account as it stands now. A card removed
    /// after the money moved must not be able to look like a charge that never happened, and a
    /// replay must reach the same provider customer and card the original attempt did or it is not
    /// a replay at all.
    /// </para>
    /// </remarks>
    private async Task<bool> ReplayAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reservation.StoredPaymentMethodId) ||
            string.IsNullOrWhiteSpace(reservation.ProviderName))
        {
            // Nothing to replay with, and no way to establish what happened. Held rather than
            // released: releasing would be a guess, and the guess costs the subscriber either their
            // money or their units.
            return false;
        }

        var charge = await _gateway.ChargeAsync(
            SettlementCharge.RequestFor(subscription, reservation),
            SettlementCharge.KeyFor(subscription, reservation),
            reservation.CorrelationId,
            cancellationToken);

        if (charge.IsSuccess)
        {
            return await PromoteAsync(subscription, reservation, charge.Value, cancellationToken);
        }

        if (!SettledFailureKinds.Contains(charge.FailureKind))
        {
            // Unanswered again. Held for the next pass rather than resolved either way.
            return false;
        }

        // The provider answered, and its answer is that no money moved — including the case where
        // the card has since been deleted, which it refuses rather than collects. That answer, not
        // the state of the billing account, is what releases a reservation.
        return await ReleaseAsync(
            subscription, reservation, "the provider refused the charge", cancellationToken);
    }

    /// <summary>
    /// The charge a reservation raised, under either name a gateway may have recorded it.
    /// </summary>
    /// <remarks>
    /// A card charge is recorded under the key the attempt reserved. An invoice that was already
    /// paid when it was finalized is recorded under the settlement key instead, because the money
    /// had moved before there was anything to reserve. Which of the two applies is a property of the
    /// provider the subscriber is on, not of anything visible here — so both are asked for. Looking
    /// under only one is how a subscriber who has paid has their reservation released as if they
    /// had not.
    /// </remarks>
    private async Task<PaymentDetail?> FindChargeAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        CancellationToken cancellationToken)
    {
        var chargeKey = SubscriptionConstants.SettlementChargeKeyFor(
            subscription.ItemId,
            reservation.ReservationId);

        return await _payments.GetByIdempotencyKeyAsync(
                   subscription.TenantId,
                   chargeKey,
                   cancellationToken)
               ?? await _payments.GetByIdempotencyKeyAsync(
                   subscription.TenantId,
                   SubscriptionConstants.RecordedSettlementKeyFor(chargeKey),
                   cancellationToken);
    }

    private async Task<bool> PromoteAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string? paymentDetailId,
        CancellationToken cancellationToken)
    {
        if (!await ApplyAsync(subscription, reservation, paymentDetailId, cancellationToken))
        {
            // Someone else settled it between the read and the write, which is the outcome this was
            // trying to reach anyway.
            return false;
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        if (_documents is not null && paymentDetailId is { Length: > 0 } invoiced)
        {
            await _documents.AnnounceChargeAsync(
                subscription,
                invoiced,
                reservation.Kind == SettlementReservationKind.PlanChange
                    ? SubscriptionChargeKind.PlanChange
                    : SubscriptionChargeKind.QuantityChange,
                null,
                reservation.CorrelationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(reservation.RequestedByUserId));
        }

        _logger.LogWarning(
            "Applied a subscription change whose charge was never recorded by its caller " +
            "Kind={Kind} TenantId={TenantId} SubscriptionId={SubscriptionId} " +
            "CorrelationId={CorrelationId}",
            reservation.Kind,
            PaymentLogValue.Id(subscription.TenantId),
            PaymentLogValue.Id(subscription.ItemId),
            PaymentLogValue.Id(reservation.CorrelationId));

        return true;
    }

    /// <summary>
    /// Writes whatever the reservation was holding the subscription for, addressed by the
    /// reservation rather than by a version.
    /// </summary>
    /// <remarks>
    /// The terms come from the reservation, never from the catalogue as it stands now. A plan whose
    /// price was edited in between must still deliver what the customer was quoted and has paid for.
    /// </remarks>
    private Task<bool> ApplyAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string? paymentDetailId,
        CancellationToken cancellationToken) =>
        reservation switch
        {
            { Kind: SettlementReservationKind.PlanChange, PlanChange: { } plan } =>
                ApplyPlanChangeAsync(
                    subscription, reservation, plan, paymentDetailId, cancellationToken),
            { Kind: SettlementReservationKind.QuantityIncrease, QuantityChange: { } quantity } =>
                _subscriptions.TryPromoteQuantityReservationAsync(
                    subscription.TenantId,
                    subscription.ItemId,
                    reservation.ReservationId,
                    quantity.RequestedQuantities,
                    quantity.NewCreditBalanceMinor,
                    paymentDetailId,
                    _events.CreateQuantityChanged(subscription, reservation.CorrelationId),
                    cancellationToken,
                    quantity.ReplacementPendingAnnualPeriod),
            // A reservation with no payload cannot be applied and must not be released either: it
            // may have money behind it. Held for the alert below.
            _ => Task.FromResult(false)
        };

    /// <summary>
    /// Installs the plan the reservation paid for, and announces it as the plan now in force.
    /// </summary>
    /// <remarks>
    /// The subscription is moved onto the target before the event is built, because the lifecycle
    /// payload reads <c>PlanCode</c> from whatever the subscription says at that moment. Built from
    /// the subscription as loaded, the event would name the plan being left as the plan arrived at,
    /// and a consumer acting on it would never learn that a paid change had happened at all — the
    /// one thing this recovery exists to guarantee. The request path mutates in the same order for
    /// the same reason.
    /// </remarks>
    private Task<bool> ApplyPlanChangeAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        ReservedPlanChange plan,
        string? paymentDetailId,
        CancellationToken cancellationToken)
    {
        var previousPlanCode = subscription.Plan.Code;

        // In memory only: this copy is discarded when the pass ends, and the write below is what
        // persists the same terms.
        subscription.Plan = plan.Plan;
        subscription.Price = plan.Price;
        subscription.QuantityItems = plan.QuantityItems;
        subscription.CreditBalanceMinor = plan.NewCreditBalanceMinor;
        subscription.PendingAnnualPeriod = plan.ReplacementPendingAnnualPeriod ?? subscription.PendingAnnualPeriod;

        return _subscriptions.TryChangePlanAsync(
            subscription.TenantId,
            subscription.ItemId,
            subscription.Version,
            reservation.ReservationId,
            plan.Plan,
            plan.Price,
            plan.QuantityItems,
            plan.Schedule,
            plan.OutgoingUsagePeriod,
            plan.NewCreditBalanceMinor,
            paymentDetailId,
            _events.CreatePlanChanged(subscription, previousPlanCode, reservation.CorrelationId),
            cancellationToken,
            replacementPendingAnnualPeriod: plan.ReplacementPendingAnnualPeriod);
    }

    private async Task<bool> ReleaseAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!await _subscriptions.TryReleaseSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                reservation.ReservationId,
                cancellationToken))
        {
            return false;
        }

        _logger.LogInformation(
            "Released an abandoned subscription quantity reservation because {Reason} " +
            "TenantId={TenantId} SubscriptionId={SubscriptionId} " +
            "CorrelationId={CorrelationId}",
            reason,
            PaymentLogValue.Id(subscription.TenantId),
            PaymentLogValue.Id(subscription.ItemId),
            PaymentLogValue.Id(reservation.CorrelationId));

        return true;
    }

    private void WarnIfLongUnresolved(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        DateTime nowUtc,
        int graceMinutes)
    {
        if (reservation.ReservedAtUtc > nowUtc.AddMinutes(-graceMinutes * GraceWindowsBeforeAlerting))
        {
            return;
        }

        _logger.LogError(
            "A subscription quantity reservation has gone unresolved long enough to need a person: " +
            "its charge is neither confirmed nor refused, and the subscriber cannot change " +
            "quantity or plan until it clears TenantId={TenantId} " +
            "SubscriptionId={SubscriptionId} ReservedAtUtc={ReservedAtUtc} " +
            "CorrelationId={CorrelationId}",
            PaymentLogValue.Id(subscription.TenantId),
            PaymentLogValue.Id(subscription.ItemId),
            reservation.ReservedAtUtc,
            PaymentLogValue.Id(reservation.CorrelationId));
    }
}
