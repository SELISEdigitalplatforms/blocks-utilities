using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Changes how many units a live subscription has bought, without changing what it is on.
/// </summary>
/// <remarks>
/// The two directions are not symmetrical, because the money is not.
/// <list type="bullet">
/// <item>An <b>increase</b> hands over the units immediately, so it is charged immediately — the
/// prorated difference for what remains of the paid period, taken before the quantity moves. A
/// declined card leaves the subscription exactly as it was.</item>
/// <item>A <b>decrease</b> is not refunded, so it cannot take effect on request: the units are
/// paid for until the period ends and the subscriber keeps them. It is held as a pending change
/// and applied by the renewal.</item>
/// </list>
/// <para>
/// Both directions price through <see cref="QuantityDiscountCalculator"/>, so crossing a volume
/// band is an ordinary consequence of the quantity moving rather than a separate operation.
/// </para>
/// </remarks>
public sealed class SubscriptionQuantityChangeService : ISubscriptionQuantityChangeService
{
    private static readonly SubscriptionStatus[] EligibleStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active
    ];

    /// <summary>
    /// Failures that state plainly that no money moved, and so may give the reservation back.
    /// </summary>
    /// <remarks>
    /// Everything absent from this list — a timeout, an unreachable provider, an unexpected
    /// exception — is an <em>unanswered</em> charge, not a declined one. The provider may have
    /// collected and lost the reply. Releasing on those would let the next attempt open a fresh
    /// reservation, raise a second charge under a new key, and take the money twice.
    /// </remarks>
    private static readonly PaymentFailureKind[] SettledFailureKinds =
    [
        PaymentFailureKind.ProviderRejected,
        PaymentFailureKind.Validation,
        PaymentFailureKind.NotFound,
        PaymentFailureKind.Conflict,
        PaymentFailureKind.RateLimited
    ];

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly IValidator<ChangeQuantityRequest> _validator;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly ILogger<SubscriptionQuantityChangeService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionQuantityChangeService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        IValidator<ChangeQuantityRequest> validator,
        ILogger<SubscriptionQuantityChangeService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null,
        ISubscriptionFinancialDocumentAnnouncer? announcer = null,
        ISubscriptionFinancialDocumentIssuer? documents = null,
        ISubscriptionBillingProfileGuard? billingProfile = null)
    {
        _scheduler = scheduler;
        _announcer = announcer;
        _documents = documents;
        _billingProfile = billingProfile;
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Announces the settlement invoice. Optional, like the scheduler beside it.</summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _announcer;

    /// <summary>
    /// Issues the credit note for an increase that banked value instead of costing anything — a
    /// volume band reached by adding units can leave the period cheaper than it was.
    /// </summary>
    private readonly ISubscriptionFinancialDocumentIssuer? _documents;

    /// <summary>
    /// Whether there is anybody to address the increase's invoice to. Optional, like the scheduler.
    /// </summary>
    private readonly ISubscriptionBillingProfileGuard? _billingProfile;

    public Task<SubscriptionOperationResult<QuantityChangeResponse>> PreviewAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken) =>
        RunAsync(subscriptionId, request, preview: true, correlationId, cancellationToken);

    public Task<SubscriptionOperationResult<QuantityChangeResponse>> ChangeAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken) =>
        RunAsync(subscriptionId, request, preview: false, correlationId, cancellationToken);

    public async Task<SubscriptionOperationResult<QuantityChangeResponse>> CancelPendingAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(subscriptionId, organizationId, correlationId, cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<QuantityChangeResponse>();
        }

        var subscription = loaded.Value!.Subscription;

        if (subscription.PendingQuantityChange is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_pending_quantity_change_not_found",
                "There is no scheduled quantity change to cancel.",
                correlationId);
        }

        if (!await _subscriptions.TryClearPendingQuantityChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription,
                subscription.QuantityItems,
                subscription.Version + 1,
                preview: false,
                immediate: true,
                effectiveAtUtc: _time.GetUtcNow().UtcDateTime,
                proratedChargeMinor: 0,
                paymentDetailId: null,
                pending: null),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> RunAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        bool preview,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation
            .CheckAsync<ChangeQuantityRequest, QuantityChangeResponse>(
                _validator,
                request,
                "subscription_quantity_invalid",
                "The quantity change request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var loaded = await LoadAsync(
            subscriptionId,
            request.OrganizationId,
            correlationId,
            cancellationToken);

        if (!loaded.IsSuccess)
        {
            return loaded.ToFailure<QuantityChangeResponse>();
        }

        var subscription = loaded.Value!.Subscription;
        var requestedByUserId = loaded.Value!.UserId;

        if (!EligibleStatuses.Contains(subscription.Status))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_quantity_change_not_allowed",
                "This subscription cannot change quantity in its current state.",
                correlationId);
        }

        // Checked before anything is calculated, so a stale caller is told to re-read rather than
        // shown a quote derived from a quantity that has already moved.
        if (subscription.Version != request.Version)
        {
            return VersionConflict(correlationId);
        }

        // A free-opening-period campaign is a fixed offer at a fixed quantity -- the temporary
        // entitlement it granted was sized for the plan and quantity the subscriber was on when it
        // was accepted. The lock lifts by itself the instant CurrentPeriodEndUtc passes, the same
        // clock check the entitlement override reads elsewhere; nothing has to run at that moment
        // to lift it. Preview is not locked.
        var promotionChangeLocked =
            subscription.Discount is { Campaign.Kind: CampaignKind.FreeOpeningCalendarPeriod } &&
            _time.GetUtcNow().UtcDateTime < subscription.CurrentPeriodEndUtc;

        if (!preview && promotionChangeLocked)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_promotion_change_locked",
                "This subscription is on a free opening period and cannot change quantity until it ends.",
                correlationId);
        }

        // One pending commercial change at a time — the mirror of the check the plan-change
        // service makes against a scheduled quantity change. Both reprice the period the next
        // renewal charges for, and a quantity scheduled against the plan being left would be
        // applied to whichever plan the boundary actually installs.
        if (!preview && subscription.PendingPlanChange is not null)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_pending_plan_change_exists",
                "A plan change is already scheduled for the end of this period. Cancel it before "
                    + "changing quantity.",
                correlationId);
        }

        // An increase already holds units and may yet be paid for. A second change quoted against
        // them would be quoting against a quantity that is halfway to being someone else's.
        if (!preview && subscription.SettlementReservation is not null)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_quantity_change_in_flight",
                "A quantity change is already being settled on this subscription.",
                correlationId);
        }


        var target = BuildTargetQuantities(subscription, request, out var unknownItemKey);

        if (unknownItemKey is not null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_item_unknown",
                "The plan does not define this quantity item.",
                correlationId);
        }

        if (OutOfBounds(subscription, target, out var offendingKey))
        {
            _logger.LogInformation(
                "Subscription quantity change refused as out of bounds " +
                "SubscriptionHash={SubscriptionHash} Item={Item}",
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(offendingKey));

            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The requested quantity is outside what this plan permits.",
                correlationId);
        }

        var effective = EffectiveQuantities(subscription);

        if (SameQuantities(effective, target))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_unchanged",
                "The requested quantity is already the effective quantity.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        // Direction is decided by the item the price is written against, never by the sum of every
        // item. Only one item carries money — GrossAmountMinor and QuantityDiscountCalculator both
        // filter to Price.QuantityItemKey — so summing lets a free item outvote a priced one and
        // send a genuine reduction down the immediate path, taking away seats the subscriber has
        // already paid for. A change that moves only free items reaches IncreaseAsync, prices at
        // zero and applies at once, which is what an increase costing nothing should do.
        var direction =
            QuantityDiscountCalculator.PricedUnits(subscription.Price, target)
                .CompareTo(QuantityDiscountCalculator.PricedUnits(subscription.Price, effective));

        // Only an increase is refused inside an opening stub, and only for a real change.
        //
        // This guard used to fire before the direction was known, which refused decreases too — and
        // a decrease in an opening stub is the case that most needs to work: it takes nothing away
        // now, schedules for the end of what was actually paid for, and moves no money at all. The
        // two halves also fail for different reasons, so they no longer share one message.
        if (!preview && direction >= 0 && subscription.PendingAnnualPeriod is { } opening)
        {
            return opening.IsPrepaid
                ? Failure(
                    PaymentFailureKind.Conflict,
                    "subscription_initial_annual_period_prepaid",
                    "This subscription has already paid for its first year, so units cannot be " +
                    "added until that year begins. Reducing units can be scheduled now.",
                    correlationId)
                : Failure(
                    PaymentFailureKind.Conflict,
                    "subscription_initial_annual_period_unpaid",
                    "This subscription's first year has not been charged yet, so units cannot be " +
                    "added until it is. Reducing units can be scheduled now.",
                    correlationId);
        }

        // Asked of a real increase only. A preview moves no money, and a decrease is scheduled for
        // the period end and never charges — refusing either over an incomplete profile would block
        // an operation that produces no document.
        if (!preview && direction >= 0 && _billingProfile is not null)
        {
            var missing = await _billingProfile.MissingFieldsAsync(
                subscription.TenantId,
                subscription.OrganizationId,
                cancellationToken);

            if (missing.Count > 0)
            {
                return SubscriptionOperationResult<QuantityChangeResponse>.Failure(
                    PaymentFailureKind.Validation,
                    "subscription_billing_profile_incomplete",
                    "This organization's billing profile is missing details an invoice must "
                        + "carry. Complete it before adding units.",
                    correlationId,
                    new Dictionary<string, string[]> { ["BillingProfile"] = [.. missing] });
            }

            await _billingProfile.RememberInitiatorAsync(
                subscription.TenantId,
                subscription.OrganizationId,
                requestedByUserId,
                loaded.Value!.UserName,
                loaded.Value!.UserEmail,
                cancellationToken);
        }

        return direction < 0
            ? await DecreaseAsync(
                subscription, target, requestedByUserId, preview, now, correlationId, cancellationToken)
            : await IncreaseAsync(
                subscription, target, requestedByUserId, preview, now, correlationId, cancellationToken);
    }

    /// <summary>
    /// An increase: priced for the remainder of the paid period, reserved, then charged.
    /// </summary>
    /// <remarks>
    /// The units are reserved before the card is charged and granted only once it settles, so
    /// there is no window in which money has moved and nothing records why. A declined card
    /// releases the reservation and the subscription stands exactly as it did.
    /// <para>
    /// The charge still comes before the units are usable. Granting them and then billing would
    /// leave a declined card holding seats it never paid for, and taking those back afterwards is
    /// a worse experience than never having been given them.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> IncreaseAsync(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        string? requestedByUserId,
        bool preview,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            // The same plan: a quantity change moves the quantity, never the plan. The bands and
            // the combination policy on both sides are the ones the subscriber already holds.
            subscription.Plan,
            subscription.Price,
            target,
            now,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc);

        if (preview)
        {
            return SubscriptionOperationResult<QuantityChangeResponse>.Success(
                Describe(
                    subscription, target, subscription.Version, preview: true, immediate: true,
                    effectiveAtUtc: now, proratedChargeMinor: outcome.ChargeMinor,
                    paymentDetailId: null, pending: null),
                correlationId);
        }

        // Nothing to reserve against: an increase that costs nothing cannot be half-paid, so it
        // takes the ordinary compare-and-set. A band that makes more units cheaper lands here too.
        if (outcome.ChargeMinor <= 0)
        {
            return await ApplyFreeIncreaseAsync(
                subscription,
                target,
                // Clamped so the balance can only fall. Credit spent bringing this change down to
                // nothing is real and must persist, but an increase that reaches a cheaper band
                // must not hand back the difference as new credit: a change that costs nothing
                // costs nothing, and banking value for it is a refund with another name.
                Math.Min(subscription.CreditBalanceMinor, outcome.NewCreditBalanceMinor),
                now,
                requestedByUserId,
                SettlementCharge.BreakdownOf(outcome),
                correlationId,
                cancellationToken);
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_payment_method_missing",
                "This increase cannot be charged without a saved payment method.",
                correlationId);
        }

        var reservation = new SettlementReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            Kind = SettlementReservationKind.QuantityIncrease,
            QuantityChange = new ReservedQuantityChange
            {
                RequestedQuantities = target,
                NewCreditBalanceMinor = outcome.NewCreditBalanceMinor
            },
            ChargeAmountMinor = outcome.ChargeMinor,
            Settlement = SettlementCharge.BreakdownOf(outcome),
            BillingAccountId = subscription.BillingAccountId,
            ProviderName = account.ProviderName,
            ProviderOrganizationId = account.ProviderOrganizationId,
            ProviderCustomerId = account.ProviderCustomerId,
            StoredPaymentMethodId = account.DefaultPaymentMethodId,
            ReservedAtUtc = now,
            RequestedByUserId = requestedByUserId,
            CorrelationId = correlationId,
            ReservedAtVersion = subscription.Version
        };

        // The one versioned write, taken while nothing has been spent. Losing it costs the caller
        // a re-read and nothing else.
        if (!await _subscriptions.TryReserveSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                reservation,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }

        // Announced before the charge, so a reservation that is written and then stranded by a
        // dying process is already known about. Best effort inside the scheduler: this must not be
        // able to fail the change it is announcing.
        if (_scheduler is not null)
        {
            await _scheduler.ScheduleReservationRecoveryAsync(
                subscription, reservation, correlationId, cancellationToken);
        }

        var charge = await _gateway.ChargeAsync(
            SettlementCharge.RequestFor(subscription, reservation),
            SettlementCharge.KeyFor(subscription, reservation),
            correlationId,
            cancellationToken);

        if (!charge.IsSuccess)
        {
            _logger.LogWarning(
                "Subscription quantity increase was not charged TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Kind={Kind} Reason={Reason}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                charge.FailureKind,
                PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

            if (!SettledFailureKinds.Contains(charge.FailureKind))
            {
                // Nobody knows whether the money moved. The reservation stays, so the next
                // attempt is refused as in flight rather than charging again, and the sweep
                // resolves it by asking the payment module what the provider actually did.
                _logger.LogError(
                    "A subscription quantity increase left its charge unanswered and is held for " +
                    "reconciliation SubscriptionHash={SubscriptionHash} Kind={Kind}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    charge.FailureKind);

                return Failure(
                    // The provider's own kind, so the caller sees 502, 503 or 504 rather than a
                    // decline it can retry straight into a second charge.
                    charge.FailureKind,
                    "subscription_quantity_charge_unresolved",
                    "The charge for the additional units could not be confirmed. " +
                    "Re-read the subscription before trying again.",
                    correlationId);
            }

            await ReleaseAsync(subscription, reservation, cancellationToken);

            // A stable code rather than the provider's own. A client renders one message for a
            // declined increase; whichever acquirer word came back belongs in the log, above.
            return Failure(
                PaymentFailureKind.ProviderRejected,
                "subscription_quantity_charge_failed",
                "The payment method declined the charge for the additional units.",
                correlationId);
        }

        if (!await PromoteAsync(subscription, reservation, charge.Value, correlationId, cancellationToken))
        {
            // The claim is gone, so something else already settled it - the recovery sweep, or a
            // retry of this same request. Either way the units are granted exactly once and the
            // caller is told to re-read rather than shown a version it cannot trust.
            return VersionConflict(correlationId);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        await AnnounceAsync(
            subscription, charge.Value, requestedByUserId, correlationId, cancellationToken);

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription,
                target,
                // Two writes: the reservation, then its promotion.
                subscription.Version + 2,
                preview: false,
                immediate: true,
                effectiveAtUtc: now,
                proratedChargeMinor: outcome.ChargeMinor,
                paymentDetailId: charge.Value,
                pending: null),
            correlationId);
    }

    /// <summary>An increase with no money attached, applied as an ordinary compare-and-set.</summary>
    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> ApplyFreeIncreaseAsync(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        long newCreditBalanceMinor,
        DateTime now,
        string? requestedByUserId,
        SubscriptionSettlementBreakdown? settlement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outboxEvent = _events.CreateQuantityChanged(subscription, correlationId);

        // No credit note, because no credit is banked. An increase reaching a cheaper volume band
        // used to hand the difference back as credit; it now applies at zero and leaves the balance
        // where it was. The caller asked for more units, not for money back, and a decrease is
        // never refunded either — paying nothing extra for a better rate is the whole benefit.
        //
        // The balance can still fall here: credit spent bringing the charge down to nothing arrives
        // already deducted in newCreditBalanceMinor, which the caller clamps so it can only
        // decrease. That consumption is recorded by the settlement on the charge itself.

        if (!await _subscriptions.TryApplyQuantityChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                target,
                newCreditBalanceMinor,
                null,
                outboxEvent,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }


        if (_scheduler is not null)
        {
            await _scheduler.ScheduleOutboxPublicationAsync(
                subscription,
                outboxEvent,
                cancellationToken);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        // Nothing to announce: this path issues no credit note any more, so there is no pending
        // document for the announcer to hurry along.

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription, target, subscription.Version + 1, preview: false, immediate: true,
                effectiveAtUtc: now, proratedChargeMinor: 0,
                paymentDetailId: null, pending: null),
            correlationId);
    }

    /// <summary>
    /// Announces the settlement's invoice, without letting that failure reach the caller.
    /// </summary>
    /// <remarks>
    /// The units are granted and the card is charged by the time this runs. A queue write that fails
    /// costs a later invoice, which the repair sweep finds; failing the request would cost the
    /// subscriber a change they have paid for.
    /// </remarks>
    private async Task AnnounceAsync(
        SubscriptionDetail subscription,
        string? paymentDetailId,
        string? requestedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_announcer is null || paymentDetailId is not { Length: > 0 } invoiced)
        {
            return;
        }

        try
        {
            await _announcer.AnnounceChargeAsync(
                subscription,
                invoiced,
                SubscriptionChargeKind.QuantityChange,
                null,
                correlationId,
                cancellationToken,
                SubscriptionDocumentSourceFactory.ActorOf(requestedByUserId));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A quantity change settled but its invoice could not be announced " +
                "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.ItemId),
                correlationId);
        }
    }

    private async Task<bool> PromoteAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        string? paymentDetailId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var outboxEvent = _events.CreateQuantityChanged(subscription, correlationId);
        var promoted = await _subscriptions.TryPromoteQuantityReservationAsync(
            subscription.TenantId,
            subscription.ItemId,
            reservation.ReservationId,
            reservation.QuantityChange!.RequestedQuantities,
            reservation.QuantityChange!.NewCreditBalanceMinor,
            paymentDetailId,
            outboxEvent,
            cancellationToken);

        if (promoted && _scheduler is not null)
        {
            await _scheduler.ScheduleOutboxPublicationAsync(
                subscription,
                outboxEvent,
                cancellationToken);
        }

        return promoted;
    }


    private async Task ReleaseAsync(
        SubscriptionDetail subscription,
        SettlementReservation reservation,
        CancellationToken cancellationToken)
    {
        if (await _subscriptions.TryReleaseSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                reservation.ReservationId,
                cancellationToken))
        {
            return;
        }

        // Left for the sweep rather than retried here: it asks the payment module what became of
        // the charge, which is the only thing that can tell a lost release from a settled reservation.
        _logger.LogError(
            "A declined subscription quantity increase could not release its reservation " +
            "SubscriptionHash={SubscriptionHash}",
            PaymentLogValue.Hash(subscription.ItemId));
    }

    /// <summary>
    /// The instant the subscriber stops having paid for what they hold.
    /// </summary>
    /// <remarks>
    /// Normally the current period's end. Not so inside a calendar-aligned opening stub with a
    /// prepaid year: <see cref="SubscriptionDetail.CurrentPeriodEndUtc"/> is then the upcoming 1st,
    /// which is the end of the <em>stub</em> and not of the year that has already been paid for.
    /// Scheduling a decrease there would have taken seats away about a month after signup, in the
    /// middle of an annual commitment the subscriber had settled in full — a refund by another
    /// name, and the one thing a decrease must never be.
    /// <para>
    /// An unpaid <c>AtBoundary</c> year is deliberately not treated this way. Nothing has been paid
    /// beyond the stub yet, so the stub's end really is the end of what was bought.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Shared with the plan-change service, which schedules against the identical instant: both
    /// are asking the same question — when does the time the subscriber already paid for run out —
    /// and two copies of that answer would eventually disagree.
    /// </remarks>
    private static DateTime PaidThrough(SubscriptionDetail subscription) =>
        SubscriptionPaidPeriod.PaidThroughUtc(subscription);

    /// <summary>
    /// A decrease: scheduled for the end of the paid period, never refunded.
    /// </summary>
    private async Task<SubscriptionOperationResult<QuantityChangeResponse>> DecreaseAsync(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        string? requestedByUserId,
        bool preview,
        DateTime now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var pending = new PendingQuantityChange
        {
            RequestedQuantities = target,
            RequestedAtUtc = now,
            EffectiveAtUtc = PaidThrough(subscription),
            RequestedByUserId = requestedByUserId,
            ExpectedVersion = subscription.Version
        };

        if (preview)
        {
            return SubscriptionOperationResult<QuantityChangeResponse>.Success(
                Describe(
                    subscription, target, subscription.Version, preview: true, immediate: false,
                    effectiveAtUtc: pending.EffectiveAtUtc, proratedChargeMinor: 0,
                    paymentDetailId: null, pending: pending),
                correlationId);
        }

        if (!await _subscriptions.TrySetPendingQuantityChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                pending,
                cancellationToken))
        {
            return VersionConflict(correlationId);
        }

        return SubscriptionOperationResult<QuantityChangeResponse>.Success(
            Describe(
                subscription, target, subscription.Version + 1, preview: false, immediate: false,
                effectiveAtUtc: pending.EffectiveAtUtc, proratedChargeMinor: 0,
                paymentDetailId: null, pending: pending),
            correlationId);
    }

    /// <summary>A subscription and the caller who asked for it, which the audit trail needs.</summary>
    private sealed record Loaded(
        SubscriptionDetail Subscription,
        string? UserId,
        string? UserName,
        string? UserEmail);

    private async Task<SubscriptionOperationResult<Loaded>> LoadAsync(
        string subscriptionId,
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
            return resolution.ToFailure<Loaded>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        return subscription is null
            ? SubscriptionOperationResult<Loaded>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId)
            : SubscriptionOperationResult<Loaded>.Success(
                new Loaded(
                    subscription,
                    context.UserId,
                    context.UserName,
                    context.UserEmail),
                correlationId);
    }

    /// <summary>
    /// The requested quantities merged over what the subscription holds, so a request naming one
    /// item leaves the rest alone.
    /// </summary>
    private static List<SubscriptionQuantityItem> BuildTargetQuantities(
        SubscriptionDetail subscription,
        ChangeQuantityRequest request,
        out string? unknownItemKey)
    {
        unknownItemKey = null;

        var target = subscription.QuantityItems
            .Select(item => new SubscriptionQuantityItem
            {
                ItemKey = item.ItemKey,
                UnitLabel = item.UnitLabel,
                Quantity = item.Quantity,
                UnitAmountMinor = item.UnitAmountMinor
            })
            .ToList();

        foreach (var requested in request.Quantities)
        {
            var held = target.Find(item =>
                string.Equals(item.ItemKey, requested.ItemKey, StringComparison.Ordinal));

            if (held is null)
            {
                unknownItemKey = requested.ItemKey;
                return target;
            }

            held.Quantity = requested.Quantity;
        }

        return target;
    }

    /// <summary>
    /// The quantities in force for pricing purposes — a pending decrease has not happened yet, so
    /// what is on the subscription is still what the subscriber holds.
    /// </summary>
    private static List<SubscriptionQuantityItem> EffectiveQuantities(
        SubscriptionDetail subscription) => subscription.QuantityItems;

    /// <summary>Bounds come from the snapshot, not the catalogue, like every other plan term.</summary>
    private static bool OutOfBounds(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        out string offendingKey)
    {
        foreach (var item in target)
        {
            var defined = subscription.Plan.QuantityItems.Find(candidate =>
                string.Equals(candidate.ItemKey, item.ItemKey, StringComparison.Ordinal));

            if (defined is null ||
                item.Quantity < defined.MinQuantity ||
                (defined.MaxQuantity is { } maximum && item.Quantity > maximum))
            {
                offendingKey = item.ItemKey;
                return true;
            }
        }

        offendingKey = string.Empty;
        return false;
    }

    private static bool SameQuantities(
        IReadOnlyList<SubscriptionQuantityItem> left,
        IReadOnlyList<SubscriptionQuantityItem> right) =>
        left.Count == right.Count &&
        left.All(item => right.Any(other =>
            string.Equals(other.ItemKey, item.ItemKey, StringComparison.Ordinal) &&
            other.Quantity == item.Quantity));

    /// <summary>
    /// The units the recurring amount is actually calculated from — those matching the price's
    /// quantity item. Zero for a flat-fee price, where no quantity moves any money at all.
    /// </summary>
    private QuantityChangeResponse Describe(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target,
        int version,
        bool preview,
        bool immediate,
        DateTime effectiveAtUtc,
        long proratedChargeMinor,
        string? paymentDetailId,
        PendingQuantityChange? pending)
    {
        var current = QuantityDiscountCalculator.ResolveFrom(
            subscription.Plan,
            subscription.Price,
            subscription.QuantityItems);

        var next = QuantityDiscountCalculator.ResolveFrom(
            subscription.Plan,
            subscription.Price,
            target);

        // What the next renewal charges, priced through the same path the renewal itself uses so
        // the figure shown cannot drift from the figure taken.
        var now = _time.GetUtcNow().UtcDateTime;
        var atTarget = CloneAtQuantities(subscription, target);
        var renewal = SubscriptionAmountCalculator.PeriodAmountMinor(atTarget, now);

        return new QuantityChangeResponse
        {
            SubscriptionId = subscription.ItemId,
            Version = version,
            Preview = preview,
            Timing = immediate ? "Immediate" : "NextPeriod",
            EffectiveAtUtc = effectiveAtUtc,
            CurrencyCode = subscription.CurrencyCode,
            Quantities = target.Select(QuantityResponseMapper.Item).ToList(),
            CurrentTier = QuantityResponseMapper.Tier(current.Tier),
            TargetTier = QuantityResponseMapper.Tier(next.Tier),
            ProratedChargeMinor = proratedChargeMinor,
            NextRenewalAmountMinor = renewal.AmountMinor,
            EffectiveUnitAmountMinor = EffectiveUnitAmount(atTarget, now),
            TaxAmountMinor = renewal.TaxAmountMinor,
            NetAmountMinor = renewal.NetAmountMinor,
            CreditConsumedMinor = renewal.CreditConsumedMinor,
            TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
            TaxMode = SubscriptionTaxPresentation.Describe(subscription.Price),
            AutomaticDiscountBasisPoints =
                SubscriptionDiscountPresentation.RateOf(subscription.Price),
            QuantityDiscountCombination =
                SubscriptionDiscountPresentation.Describe(subscription.Price),
            GrossAmountMinor = renewal.GrossAmountMinor,
            BuiltInDiscountMinor = renewal.BuiltInDiscountMinor,
            PromotionalDiscountMinor = renewal.PromotionalDiscountMinor,
            DiscountedAmountMinor = renewal.GrossAmountMinor
                - renewal.BuiltInDiscountMinor
                - renewal.PromotionalDiscountMinor,
            PromotionApplied = renewal.DiscountApplied,
            Blockers = preview &&
                subscription.Discount is { Campaign.Kind: CampaignKind.FreeOpeningCalendarPeriod } &&
                now < subscription.CurrentPeriodEndUtc
                    ?
                    [
                        new SubscriptionPreviewBlockerResponse
                        {
                            Code = "subscription_promotion_change_locked",
                            Message = "This subscription is on a free opening period and cannot change quantity until it ends."
                        }
                    ]
                    : [],
            ChargePaymentDetailId = paymentDetailId,
            PendingQuantityChange = QuantityResponseMapper.Pending(pending)
        };
    }

    /// <summary>
    /// What one unit costs at the target quantity, before tax, or nothing where units do not price
    /// the subscription at all.
    /// </summary>
    /// <remarks>
    /// Taken from the same calculation the charge comes from, one step earlier: after the band, the
    /// promotion and the combination policy, but before tax is added to the whole. Divided out of
    /// the tax-inclusive total instead, a 5% band on a taxed price reported a unit costing
    /// <em>more</em> than the undiscounted list price.
    /// <para>
    /// Null for a flat fee, so a caller cannot print the plan's whole price as the cost of each of
    /// something it tracks for free.
    /// </para>
    /// </remarks>
    private static long? EffectiveUnitAmount(SubscriptionDetail atTarget, DateTime nowUtc)
    {
        var units = QuantityDiscountCalculator.PricedUnits(atTarget.Price, atTarget.QuantityItems);

        if (units <= 0)
        {
            return null;
        }

        // The pre-tax figure behind the same renewal amount, so the two agree to the currency's
        // last unit once tax is added back.
        var beforeTax = SubscriptionAmountCalculator.DiscountedAmountMinor(
            atTarget.Plan,
            atTarget.Discount,
            atTarget.Price,
            atTarget.QuantityItems,
            atTarget.DiscountPeriodsApplied,
            nowUtc);

        return (long)Math.Round((decimal)beforeTax.AmountMinor / units, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The subscription as it would stand at the target quantities, for pricing only. Never
    /// persisted — a shallow copy is enough because only the quantities differ.
    /// </summary>
    private static SubscriptionDetail CloneAtQuantities(
        SubscriptionDetail subscription,
        List<SubscriptionQuantityItem> target) => new()
    {
        ItemId = subscription.ItemId,
        TenantId = subscription.TenantId,
        OrganizationId = subscription.OrganizationId,
        Status = subscription.Status,
        Plan = subscription.Plan,
        Price = subscription.Price,
        QuantityItems = target,
        CurrencyCode = subscription.CurrencyCode,
        Discount = subscription.Discount,
        DiscountPeriodsApplied = subscription.DiscountPeriodsApplied,
        // Deliberately not the credit balance: a renewal quote is what the period costs, and
        // banked credit is settled against it separately.
        CreditBalanceMinor = 0
    };



    private static SubscriptionOperationResult<QuantityChangeResponse> VersionConflict(
        string correlationId) =>
        Failure(
            PaymentFailureKind.Conflict,
            "subscription_version_conflict",
            "The subscription changed while this request was in flight. Re-read and try again.",
            correlationId);

    private static SubscriptionOperationResult<QuantityChangeResponse> Failure(
        PaymentFailureKind kind,
        string code,
        string message,
        string correlationId) =>
        SubscriptionOperationResult<QuantityChangeResponse>.Failure(kind, code, message, correlationId);
}
