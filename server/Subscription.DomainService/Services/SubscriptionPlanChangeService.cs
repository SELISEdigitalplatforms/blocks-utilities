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
/// Moves a live subscription to a different price, mid-period.
/// </summary>
/// <remarks>
/// A trial has paid nothing yet, so its plan simply swaps. Otherwise the change is priced by
/// <see cref="SubscriptionProrationCalculator"/> and, if that leaves something owing, charged
/// immediately through the same <see cref="ISubscriptionBillingGateway"/> a renewal uses — this
/// is the seam's third caller, after the renewal service and (indirectly) the checkout service's
/// sibling money path.
/// <para>
/// The target cadence starts at the change instant. Both fee and usage schedules are rebuilt so
/// changing a monthly subscription to annual cannot leave a monthly renewal clock behind.
/// </para>
/// </remarks>
public sealed class SubscriptionPlanChangeService : ISubscriptionPlanChangeService
{
    /// <summary>
    /// Failures that state plainly that no money moved, and so may release the reservation.
    /// </summary>
    /// <remarks>
    /// Everything absent from this list is an <em>unanswered</em> charge rather than a declined one.
    /// The provider may have collected and lost the reply, and releasing on those would let a retry
    /// reserve again, charge again, and take the money twice.
    /// </remarks>
    private static readonly PaymentFailureKind[] SettledFailureKinds =
    [
        PaymentFailureKind.ProviderRejected,
        PaymentFailureKind.Validation,
        PaymentFailureKind.NotFound,
        PaymentFailureKind.Conflict,
        PaymentFailureKind.RateLimited
    ];

    private static readonly SubscriptionStatus[] EligibleStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active
    ];

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionOutboxEventFactory _events;
    private readonly IEntitlementSnapshotCache _cache;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly ISubscriptionResponseMapper _mapper;
    private readonly IValidator<ChangeSubscriptionPlanRequest> _validator;
    private readonly ILogger<SubscriptionPlanChangeService> _logger;
    private readonly TimeProvider _time;
    private readonly ISubscriptionUsageRepository? _usage;
    private readonly IMeterAllowanceResolver? _allowances;

    public SubscriptionPlanChangeService(
        ISubscriptionContextResolver contextResolver,
        ISubscriptionRepository subscriptions,
        ISubscriptionCatalogueRepository catalogue,
        IBillingAccountRepository billingAccounts,
        ISubscriptionBillingGateway gateway,
        ISubscriptionOutboxEventFactory events,
        IEntitlementSnapshotCache cache,
        ISubscriptionResponseMapper mapper,
        IValidator<ChangeSubscriptionPlanRequest> validator,
        ILogger<SubscriptionPlanChangeService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null,
        ISubscriptionFinancialDocumentAnnouncer? announcer = null,
        ISubscriptionBillingProfileGuard? billingProfile = null,
        ISubscriptionUsageRepository? usage = null,
        IMeterAllowanceResolver? allowances = null,
        ISubscriptionAuditTrail? audit = null)
    {
        _audit = audit;
        _contextResolver = contextResolver;
        _subscriptions = subscriptions;
        _catalogue = catalogue;
        _billingAccounts = billingAccounts;
        _gateway = gateway;
        _events = events;
        _cache = cache;
        _scheduler = scheduler;
        _mapper = mapper;
        _validator = validator;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _announcer = announcer;
        _billingProfile = billingProfile;
        _usage = usage;
        _allowances = allowances;
    }

    /// <summary>
    /// Whether there is anybody to address this change's invoice to. Optional, like the scheduler.
    /// </summary>
    private readonly ISubscriptionBillingProfileGuard? _billingProfile;


    /// <summary>Announces the settlement invoice. Optional, like the scheduler it sits beside.</summary>
    private readonly ISubscriptionFinancialDocumentAnnouncer? _announcer;

    /// <summary>
    /// Records booking and unbooking a plan change, neither of which publishes a lifecycle event.
    /// </summary>
    /// <remarks>
    /// A scheduled change moves nothing yet, so there is no <c>PlanChanged</c> to publish and
    /// nothing downstream to react to — but somebody decided it, against a specific plan and
    /// price, to land on a specific date, and that decision is exactly the kind of thing an audit
    /// trail exists for. The renewal that eventually applies the change publishes the event.
    /// </remarks>
    private readonly ISubscriptionAuditTrail? _audit;

    /// <summary>
    /// Records one booked-or-unbooked plan change. Swallowed on failure: the decision has been
    /// written to the subscription and refusing the caller because a note could not be filed would
    /// cost them a change that already happened.
    /// </summary>
    private async Task RecordScheduleAuditAsync(
        SubscriptionDetail subscription,
        string operation,
        PendingPlanChange change,
        string? actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        try
        {
            await _audit.RecordAsync(new SubscriptionAuditEvent
            {
                TenantId = subscription.TenantId,
                OrganizationId = subscription.OrganizationId,
                SubscriptionId = subscription.ItemId,
                OperationId = correlationId,
                CorrelationId = correlationId,
                Operation = operation,
                Stage = "Completed",
                Outcome = "Succeeded",
                Source = "Api",
                UserId = actorUserId,
                CurrencyCode = subscription.CurrencyCode,
                PreviousPlanCode = subscription.Plan.Code,
                PreviousPriceId = subscription.Price.PriceId,
                TargetPlanCode = change.Plan.Code,
                TargetPriceId = change.Price.PriceId,
                EffectiveAtUtc = change.EffectiveAtUtc
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A scheduled plan change was written but its audit record was not " +
                "SubscriptionHash={SubscriptionHash} Operation={Operation} " +
                "CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.ItemId),
                PaymentLogValue.Label(operation),
                correlationId);
        }
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> ChangePlanAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(
            subscriptionId, request, preview: false, correlationId, cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.ToFailure<SubscriptionResponse>();
        }

        var r = resolved.Value!;

        return r.Subscription.Status == SubscriptionStatus.Trialing
            ? await ApplyAsync(
                r.Subscription, r.NewPlan, r.NewPrice, r.Quantities, r.NewSchedule,
                r.Subscription.CreditBalanceMinor, null, null, correlationId, cancellationToken,
                initiatedByUserId: r.RequestedByUserId)
            : await ChargeAndApplyAsync(
                r.Subscription, r.NewPlan, r.NewPrice, r.Quantities, r.NewSchedule, r.Now,
                r.RequestedByUserId, correlationId, cancellationToken);
    }

    public async Task<SubscriptionOperationResult<SubscriptionResponse>> CancelPendingPlanChangeAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _contextResolver.ResolveAsync(
            correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<SubscriptionResponse>(correlationId);
        }

        var context = resolution.Context!;
        var subscription = await _subscriptions.GetAsync(
            context.TenantId, context.OrganizationId, subscriptionId, cancellationToken);

        if (subscription is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        if (subscription.PendingPlanChange is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_pending_plan_change_not_found",
                "There is no scheduled plan change to cancel.",
                correlationId);
        }

        if (!await _subscriptions.TryClearPendingPlanChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while the scheduled plan change was being cancelled.",
                correlationId);
        }

        // Recorded before the local copy is cleared, so the entry can still name what was booked.
        await RecordScheduleAuditAsync(
            subscription, "CancelScheduledPlanChange", subscription.PendingPlanChange,
            context.UserId, correlationId, cancellationToken);

        subscription.PendingPlanChange = null;
        subscription.Version++;

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Scheduled subscription plan change cancelled TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionPlanChangePreviewResponse>>
        PreviewPlanChangeAsync(
            string subscriptionId,
            ChangeSubscriptionPlanRequest request,
            string correlationId,
            CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(
            subscriptionId, request, preview: true, correlationId, cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.ToFailure<SubscriptionPlanChangePreviewResponse>();
        }

        var r = resolved.Value!;

        var outcome = SubscriptionProrationCalculator.Calculate(
            r.Subscription,
            r.NewPlan,
            r.NewPrice,
            r.Quantities,
            r.Now,
            r.NewSchedule.CurrentPeriodStartUtc,
            r.NewSchedule.CurrentPeriodEndUtc,
            r.NewSchedule.FeePeriodFraction);

        var blockers = new List<SubscriptionPreviewBlockerResponse>(r.Blockers);

        // Quoted by the same rule the confirm applies, from the same settlement — a preview that
        // said "immediate" for a change the confirm then scheduled would be quoting a different
        // operation than the one it is previewing.
        var timing = PlanChangeClassifier.Classify(
            r.Subscription,
            r.NewPrice,
            outcome.Breakdown.Target.ProratedValueMinor -
                outcome.Breakdown.Outgoing.ProratedValueMinor);

        // The dates and schedule the change would actually land on. A scheduled change is derived
        // from the instant it becomes effective, exactly as the confirm derives it — quoting the
        // request-time schedule would show a period the subscriber is never on.
        var effectiveAtUtc = timing == PlanChangeTiming.Immediate
            ? r.Now
            : SubscriptionPaidPeriod.PaidThroughUtc(r.Subscription);
        var quotedSchedule = r.NewSchedule;

        if (timing == PlanChangeTiming.NextRenewal &&
            TryBuildSchedule(r.Subscription, r.NewPlan, r.NewPrice, effectiveAtUtc, out var scheduled))
        {
            quotedSchedule = scheduled;
        }

        // Only a change charged *today* needs a card today, and the amount owed is unaffected by
        // whether one is on file — so this is an obstacle to name alongside the price, not a
        // reason to withhold it.
        //
        // Gated on the timing rather than on the amount: a scheduled cadence change can settle to
        // a positive figure and still take nothing now, and demanding a card for it would block a
        // change that is not going to charge anybody for weeks.
        if (timing == PlanChangeTiming.Immediate && outcome.ChargeMinor > 0)
        {
            var account = await _billingAccounts.GetAsync(
                r.Subscription.TenantId,
                r.Subscription.BillingAccountId,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
            {
                blockers.Add(new SubscriptionPreviewBlockerResponse
                {
                    Code = "subscription_plan_change_no_payment_method",
                    Message = "This upgrade cannot be charged without a saved payment method."
                });
            }
        }

        return SubscriptionOperationResult<SubscriptionPlanChangePreviewResponse>.Success(
            new SubscriptionPlanChangePreviewResponse
            {
                CurrencyCode = r.Subscription.CurrencyCode,
                TargetPlanCode = r.NewPlan.Code,
                TargetPlanName = r.NewPlan.DisplayName,
                TargetPriceId = r.NewPrice.PriceId,
                Interval = r.NewPrice.Interval.ToString(),
                IntervalCount = r.NewPrice.IntervalCount,
                Quantities = [.. r.Quantities.Select(item => new SubscriptionQuantityResponse
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    Quantity = item.Quantity
                })],
                ChargeMinor = outcome.ChargeMinor,
                Timing = timing.ToString(),
                EffectiveAtUtc = effectiveAtUtc,
                // Explicitly zero. Nothing banks credit any more, and this used to report
                // NewCreditBalanceMinor — the whole balance to write, not what this change added —
                // so a subscriber already holding CHF 50 was told a downgrade had just banked
                // CHF 50 for them. Kept on the response for compatibility, and now always zero;
                // credit actually spent is reported by settlement.creditConsumedMinor.
                CreditBankedMinor = 0,
                Settlement = SettlementResponseOf(outcome.Breakdown),
                NewPeriodStartUtc = quotedSchedule.CurrentPeriodStartUtc,
                NewPeriodEndUtc = quotedSchedule.CurrentPeriodEndUtc,
                // Already the whole target period, tax included, undiminished by proration — see
                // ProrationSide.PeriodTotalMinor — so there is nothing left to compute here.
                NextRenewalAmountMinor = outcome.Breakdown.Target.PeriodTotalMinor,
                Blockers = blockers,
                QuotedAtUtc = r.Now
            },
            correlationId);
    }

    /// <summary>
    /// Everything <see cref="ChangePlanAsync"/> and <see cref="PreviewPlanChangeAsync"/> share:
    /// resolving the caller, the subscription, the target plan and price, and the schedule it
    /// would move onto. Stops short of pricing, because pricing is the one step the two callers
    /// do not share — <see cref="ChargeAndApplyAsync"/> still calls
    /// <see cref="SubscriptionProrationCalculator.Calculate"/> itself.
    /// </summary>
    /// <remarks>
    /// A condition that would refuse <see cref="ChangePlanAsync"/> without changing what a change
    /// would cost — an incomplete billing profile — is collected into <c>Blockers</c> on a preview
    /// instead of failing outright. A condition that leaves no coherent price to quote — an
    /// unsurvivable discount, an unknown target, an unbuildable schedule — fails either way, since
    /// a preview with nothing to price has nothing to show. <see cref="SettlementReservation"/> and
    /// <see cref="SubscriptionDetail.PendingAnnualPeriod"/> are checked only on the real change,
    /// mirroring <see cref="SubscriptionQuantityChangeService"/>'s own preview exactly — a quote is
    /// read-only and does not need the state that would block a write.
    /// </remarks>
    private async Task<SubscriptionOperationResult<PlanChangeResolution>> ResolveAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        bool preview,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation
            .CheckAsync<ChangeSubscriptionPlanRequest, PlanChangeResolution>(
                _validator,
                request,
                "subscription_plan_change_invalid",
                "The plan change request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var resolution = await _contextResolver.ResolveAsync(
            correlationId,
            request.OrganizationId,
            cancellationToken);

        if (!resolution.IsSuccess)
        {
            return resolution.ToFailure<PlanChangeResolution>(correlationId);
        }

        var context = resolution.Context!;

        var subscription = await _subscriptions.GetAsync(
            context.TenantId,
            context.OrganizationId,
            subscriptionId,
            cancellationToken);

        if (subscription is null)
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "The subscription does not exist.",
                correlationId);
        }

        if (!EligibleStatuses.Contains(subscription.Status))
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_not_eligible",
                "This subscription cannot change plan in its current state.",
                correlationId);
        }

        // A quantity increase is holding units priced against the plan being left. Refused by name
        // rather than by the repository's filter alone, so the caller knows to re-read and retry
        // rather than reading it as a stale version. Only enforced on the real change: a preview
        // is read-only and does not need the reservation to be clear to quote a price against the
        // subscription as it stands.
        if (!preview && subscription.SettlementReservation is not null)
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Conflict,
                "subscription_quantity_change_in_flight",
                "A quantity change is being settled on this subscription.",
                correlationId);
        }

        // A free-opening-period campaign is a fixed offer -- one calendar month on the plan it
        // named, at the temporary entitlement it named. Moving plan or quantity mid-offer would be
        // repricing a promise the subscriber was given for free, which this campaign never
        // promised to price. The lock lifts by itself the instant CurrentPeriodEndUtc passes, the
        // same clock check the entitlement override above reads -- nothing has to run at that
        // moment to lift it. Preview is not locked: showing what a change would cost is not
        // committing to one.
        var promotionChangeLocked =
            subscription.Discount is { Campaign.Kind: CampaignKind.FreeOpeningCalendarPeriod } &&
            _time.GetUtcNow().UtcDateTime < subscription.CurrentPeriodEndUtc;

        if (!preview && promotionChangeLocked)
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Conflict,
                "subscription_promotion_change_locked",
                "This subscription is on a free opening period and cannot change plan until it ends.",
                correlationId);
        }

        // One pending commercial change at a time. A scheduled quantity change and a scheduled
        // plan change both reprice the period the next renewal charges for, so holding both would
        // leave the boundary with two answers to one question — and silently replacing the one
        // already held would discard a change the subscriber was shown a quote for.
        //
        // Same-type replacement is still allowed: asking for a different plan than the one already
        // scheduled is a customer changing their mind, and that is handled by the write itself.
        if (!preview && subscription.PendingQuantityChange is not null)
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Conflict,
                "subscription_pending_quantity_change_exists",
                "A quantity change is already scheduled for the end of this period. Cancel it "
                    + "before scheduling a plan change.",
                correlationId);
        }

        // The opening-stub guard deliberately does not live here any more. It used to refuse every
        // change against a pending annual period, before anything knew whether the change would
        // charge — which refused scheduled downgrades too, and those are the case that most needs
        // to work in an opening stub: they take nothing away now and move no money at all. It is
        // asked in ChargeAndApplyAsync instead, once the timing is known, and only of a change
        // that would settle immediately.

        var terms = await ResolveTargetAsync(request, context, subscription, correlationId, cancellationToken);

        if (!terms.IsSuccess)
        {
            return terms.ToFailure<PlanChangeResolution>();
        }

        var (plan, price) = terms.Value;

        if (!DiscountSurvives(subscription, plan, price))
        {
            // Not a blocker: the real change never charges a price with the discount silently
            // dropped, it only ever refuses, so there is no honest number to show alongside this
            // one. A preview with nothing achievable to price has nothing to quote.
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Validation,
                "subscription_discount_not_applicable",
                "The discount on this subscription does not apply to the plan or price being "
                    + "moved to. Cancel the subscription and start a new one to change onto it, "
                    + "or choose a target the discount covers.",
                correlationId);
        }

        var blockers = new List<SubscriptionPreviewBlockerResponse>();

        if (preview && promotionChangeLocked)
        {
            blockers.Add(new SubscriptionPreviewBlockerResponse
            {
                Code = "subscription_promotion_change_locked",
                Message = "This subscription is on a free opening period and cannot change plan until it ends."
            });
        }

        if (await MissingBillingProfileFieldsAsync(context, preview, cancellationToken) is
            { Count: > 0 } missing)
        {
            // A plan change prorates two periods and usually charges the difference, so it is a
            // money-moving change and needs somebody to invoice. On the real change this is
            // refused here, which is the only point at which refusing is free. A preview shows the
            // price anyway — filling in the profile does not change what the change would cost.
            if (!preview)
            {
                return SubscriptionOperationResult<PlanChangeResolution>.Failure(
                    PaymentFailureKind.Validation,
                    "subscription_billing_profile_incomplete",
                    "This organization's billing profile is missing details an invoice must " +
                        "carry. Complete it before changing plan.",
                    correlationId,
                    new Dictionary<string, string[]> { ["BillingProfile"] = [.. missing] });
            }

            blockers.Add(new SubscriptionPreviewBlockerResponse
            {
                Code = "subscription_billing_profile_incomplete",
                Message = "This organization's billing profile is missing details an invoice " +
                    "must carry. Complete it before changing plan.",
                Fields = new Dictionary<string, string[]> { ["BillingProfile"] = [.. missing] }
            });
        }

        var quantities = SubscriptionQuantityBuilder.Build(request.Quantities, plan, price);

        if (quantities is null)
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The quantities do not match the plan's items or fall outside their bounds.",
                correlationId);
        }

        var newPlan = SubscriptionSnapshotBuilder.SnapshotOf(plan);
        var newPrice = SubscriptionSnapshotBuilder.SnapshotOf(price);
        var now = _time.GetUtcNow().UtcDateTime;

        if (!TryBuildSchedule(subscription, newPlan, newPrice, now, out var newSchedule))
        {
            return Failure<PlanChangeResolution>(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The target plan's schedules could not be derived.",
                correlationId);
        }

        return SubscriptionOperationResult<PlanChangeResolution>.Success(
            new PlanChangeResolution(
                subscription, newPlan, newPrice, quantities, newSchedule, now,
                context.UserId, blockers),
            correlationId);
    }

    /// <summary>
    /// Whether the subscriber's promotional discount may follow them to the target plan and price.
    /// </summary>
    /// <remarks>
    /// A plan change keeps the subscription's discount — that is the point of snapshotting it — but a
    /// code authored for the monthly price must not end up reducing the annual one. The restriction
    /// is read from the terms copied at redemption rather than from the catalogue, so a discount
    /// retired or re-scoped since then is judged by the offer the subscriber actually accepted.
    /// <para>
    /// Refused rather than silently dropped. Removing the promotion would change what the subscriber
    /// pays every period from here on, and doing that inside an operation they asked for a
    /// <em>price</em> quote on is the kind of surprise that shows up as a support ticket months
    /// later. Refusing puts the consequence in front of them first.
    /// </para>
    /// <para>
    /// Only asked of a discount that is still reducing charges. One whose duration is spent or whose
    /// expiry has passed reduces nothing, so blocking a plan change over its restrictions would be
    /// enforcing an offer that has already ended.
    /// </para>
    /// </remarks>
    private bool DiscountSurvives(SubscriptionDetail subscription, Plan plan, Price price)
    {
        if (subscription.Discount is not { } discount ||
            !SubscriptionAmountCalculator.DiscountStillActive(
                discount,
                subscription.DiscountPeriodsApplied,
                _time.GetUtcNow().UtcDateTime))
        {
            return true;
        }

        return SubscriptionDiscountApplicability.Permits(discount, plan.Code, price.ItemId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAndApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        SubscriptionPlanSchedule newSchedule,
        DateTime now,
        string? requestedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // A prepaid opening stub, moving onto a plan that keeps the subscriber's calendar boundary,
        // settles the stub and the year it already paid for together rather than being refused
        // outright — see ChargeAndApplyOpeningStubUpgradeAsync. Every other case falls through to
        // the ordinary path below: an unpaid stub is still refused there, and a prepaid stub moving
        // cadence or alignment is already routed to ScheduleAsync by Classify's own check, since
        // re-cadencing a paid annual term can never be priced against it mid-commitment.
        if (subscription.PendingAnnualPeriod is { IsPrepaid: true } prepaidAnnual &&
            !PlanChangeClassifier.ChangesCadenceOrAlignment(subscription.Price, newPrice))
        {
            var stubUpgrade = SubscriptionProrationCalculator.CalculateOpeningStubUpgrade(
                subscription,
                newPlan,
                newPrice,
                quantities,
                prepaidAnnual,
                now,
                newSchedule.FeePeriodFraction);

            // Classified on the combined delta, not the stub's alone: a nominally-positive stub
            // settlement paired with a strongly negative annual one is a real downgrade, and
            // classifying by the stub alone would charge it today instead of waiting for the year.
            if (PlanChangeClassifier.Classify(subscription, newPrice, stubUpgrade.RawSettlementMinor)
                == PlanChangeTiming.NextRenewal)
            {
                return await ScheduleAsync(
                    subscription, newPlan, newPrice, quantities, now,
                    requestedByUserId, correlationId, cancellationToken);
            }

            return await ChargeAndApplyOpeningStubUpgradeAsync(
                subscription, newPlan, newPrice, quantities, newSchedule, prepaidAnnual,
                stubUpgrade, now, requestedByUserId, correlationId, cancellationToken);
        }

        var outcome = SubscriptionProrationCalculator.Calculate(
            subscription,
            newPlan,
            newPrice,
            quantities,
            now,
            newSchedule.CurrentPeriodStartUtc,
            newSchedule.CurrentPeriodEndUtc,
            newSchedule.FeePeriodFraction);

        // What the change itself is worth for this period, before any credit balance pays for it.
        // Deliberately not outcome.ChargeMinor, which is what is left to collect *after* credit: a
        // subscriber holding enough credit to cover an upgrade has still asked for an upgrade, and
        // classifying by the charge would schedule it for next month purely because they had a
        // balance. Whether a change hands something over now is a property of the change.
        var settlementMinor =
            outcome.Breakdown.Target.ProratedValueMinor -
            outcome.Breakdown.Outgoing.ProratedValueMinor;

        if (PlanChangeClassifier.Classify(subscription, newPrice, settlementMinor)
            == PlanChangeTiming.NextRenewal)
        {
            return await ScheduleAsync(
                subscription, newPlan, newPrice, quantities, now,
                requestedByUserId, correlationId, cancellationToken);
        }

        // Only an unpaid stub reaches this any more: a prepaid one was intercepted above, whether
        // it settled immediately or was routed to ScheduleAsync. The opening charge has not
        // cleared yet, so there is nothing paid to settle a plan change against — it must wait
        // until the opening charge either succeeds (making it prepaid, and eligible for the
        // composite path above on the next attempt) or the subscription is cancelled.
        if (subscription.PendingAnnualPeriod is { IsPrepaid: false })
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_initial_annual_period_unpaid",
                "This subscription's first year has not been charged yet, so it cannot move "
                    + "onto another plan until it is. A downgrade can be scheduled now.",
                correlationId);
        }

        if (outcome.ChargeMinor <= 0)
        {
            // An upgrade the subscriber's existing credit covers in full. It applies now — they
            // asked for more and are getting it — and the balance it spent is written with it.
            return await ApplyAsync(
                subscription, newPlan, newPrice, quantities, newSchedule,
                outcome.NewCreditBalanceMinor, null, null, correlationId, cancellationToken,
                SettlementCharge.BreakdownOf(outcome), requestedByUserId);
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_no_payment_method",
                "This upgrade cannot be charged without a saved payment method.",
                correlationId);
        }

        // Reserved before anything is spent, and the charge is keyed on the reservation rather than
        // on the version. Keyed on the version, a write lost to a concurrent change left the money
        // moved and the plan unchanged, and the retry — which necessarily read a new version — built
        // a different key and charged again.
        var reservation = new SettlementReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            Kind = SettlementReservationKind.PlanChange,
            PlanChange = new ReservedPlanChange
            {
                Plan = newPlan,
                Price = newPrice,
                QuantityItems = quantities,
                Schedule = newSchedule,
                OutgoingUsagePeriod = await SnapshotOutgoingUsagePeriodAsync(
                    subscription, correlationId, cancellationToken),
                NewCreditBalanceMinor = outcome.NewCreditBalanceMinor
            },
            ChargeAmountMinor = outcome.ChargeMinor,
            Settlement = SettlementCharge.BreakdownOf(outcome),
            // Carried so the invoice can say who asked for the change, and so a settlement recovered
            // by the sweep names the same person the caller would have.
            RequestedByUserId = requestedByUserId,
            BillingAccountId = subscription.BillingAccountId,
            ProviderName = account.ProviderName,
            ProviderOrganizationId = account.ProviderOrganizationId,
            ProviderCustomerId = account.ProviderCustomerId,
            StoredPaymentMethodId = account.DefaultPaymentMethodId,
            ReservedAtUtc = now,
            CorrelationId = correlationId,
            ReservedAtVersion = subscription.Version
        };

        if (!await _subscriptions.TryReserveSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                reservation,
                cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
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
                "Subscription plan change was not charged TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Kind={Kind} Reason={Reason}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                charge.FailureKind,
                PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

            if (!SettledFailureKinds.Contains(charge.FailureKind))
            {
                // Nobody knows whether the money moved. The reservation stays, so a retry is
                // refused rather than charging again, and the sweep resolves it by asking the
                // payment module what the provider actually did.
                _logger.LogError(
                    "A subscription plan change left its charge unanswered and is held for " +
                    "reconciliation SubscriptionHash={SubscriptionHash} Kind={Kind}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    charge.FailureKind);

                return Failure(
                    charge.FailureKind,
                    "subscription_plan_change_charge_unresolved",
                    "The charge for this plan change could not be confirmed. " +
                    "Re-read the subscription before trying again.",
                    correlationId);
            }

            await ReleaseAsync(subscription, reservation, cancellationToken);

            return charge.ToFailure<SubscriptionResponse>();
        }

        return await ApplyAsync(
            subscription, newPlan, newPrice, quantities, newSchedule,
            outcome.NewCreditBalanceMinor, charge.Value, reservation.ReservationId,
            correlationId, cancellationToken,
            reservation.Settlement, requestedByUserId);
    }

    /// <summary>
    /// Settles a prepaid opening stub's remaining days and the annual period it already paid for,
    /// together, once <see cref="ChargeAndApplyAsync"/> has decided the change belongs today.
    /// </summary>
    /// <remarks>
    /// The stub itself does not move — only its price does. <paramref name="newSchedule"/> was
    /// resolved fresh at <paramref name="now"/> purely for pricing — its
    /// <see cref="SubscriptionPlanSchedule.FeePeriodFraction"/> is exactly the days remaining on
    /// the stub — but its own <c>CurrentPeriodStartUtc</c>/<c>EndUtc</c> describe a period anchored
    /// on today rather than the stub the subscriber is actually partway through. Applying it
    /// verbatim would silently move the subscription's period-start date to today, so this method
    /// keeps the subscription's own stub bounds and only swaps the recurring cadence descriptor and
    /// usage schedule the target carries going forward — the compatible-alignment guarantee the
    /// caller already checked means the derived cadence is equivalent to what is already in force.
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ChargeAndApplyOpeningStubUpgradeAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        SubscriptionPlanSchedule newSchedule,
        PendingAnnualPeriod currentAnnual,
        OpeningStubUpgradeOutcome stubUpgrade,
        DateTime now,
        string? requestedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var compositeSchedule = newSchedule with
        {
            CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc,
            CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            NextFeeBillingAtUtc = subscription.CurrentPeriodEndUtc,
            FeePeriodFraction = default
        };

        // The same dates the stub already carries — this settles what the year costs, not when it
        // runs. Everything else about it is repriced onto the target's own terms.
        var replacementAnnual = new PendingAnnualPeriod
        {
            StartUtc = currentAnnual.StartUtc,
            EndUtc = currentAnnual.EndUtc,
            AmountMinor = stubUpgrade.Annual.Target.ProratedValueMinor,
            NetAmountMinor = stubUpgrade.Annual.Target.ProratedValueMinor
                - stubUpgrade.Annual.Target.TaxAmountMinor,
            TaxAmountMinor = stubUpgrade.Annual.Target.TaxAmountMinor,
            GrossAmountMinor = stubUpgrade.Annual.Target.GrossAmountMinor,
            BuiltInDiscountMinor = stubUpgrade.Annual.Target.BuiltInDiscountMinor,
            PromotionalDiscountMinor = stubUpgrade.Annual.Target.PromotionalDiscountMinor,
            DiscountApplied = stubUpgrade.TargetAnnualDiscountApplied,
            CollectedWithCheckout = currentAnnual.CollectedWithCheckout,
            IsPrepaid = true,
            // Carried over by default — the original payment still stands behind this year unless
            // a new charge below settles the difference, in which case that charge takes its place
            // as the payment that most recently settled it.
            PaymentDetailId = currentAnnual.PaymentDetailId
        };

        if (stubUpgrade.ChargeMinor <= 0)
        {
            // Credit alone covers the combined settlement. It applies now, exactly as an ordinary
            // credit-covered upgrade does, and the balance it spent is written with it.
            return await ApplyAsync(
                subscription, newPlan, newPrice, quantities, compositeSchedule,
                stubUpgrade.NewCreditBalanceMinor, null, null, correlationId, cancellationToken,
                SettlementCharge.BreakdownOf(stubUpgrade), requestedByUserId, replacementAnnual);
        }

        var account = await _billingAccounts.GetAsync(
            subscription.TenantId,
            subscription.BillingAccountId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account?.DefaultPaymentMethodId))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_no_payment_method",
                "This upgrade cannot be charged without a saved payment method.",
                correlationId);
        }

        var reservation = new SettlementReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            Kind = SettlementReservationKind.PlanChange,
            PlanChange = new ReservedPlanChange
            {
                Plan = newPlan,
                Price = newPrice,
                QuantityItems = quantities,
                Schedule = compositeSchedule,
                OutgoingUsagePeriod = await SnapshotOutgoingUsagePeriodAsync(
                    subscription, correlationId, cancellationToken),
                NewCreditBalanceMinor = stubUpgrade.NewCreditBalanceMinor,
                ReplacementPendingAnnualPeriod = replacementAnnual
            },
            ChargeAmountMinor = stubUpgrade.ChargeMinor,
            Settlement = SettlementCharge.BreakdownOf(stubUpgrade),
            RequestedByUserId = requestedByUserId,
            BillingAccountId = subscription.BillingAccountId,
            ProviderName = account.ProviderName,
            ProviderOrganizationId = account.ProviderOrganizationId,
            ProviderCustomerId = account.ProviderCustomerId,
            StoredPaymentMethodId = account.DefaultPaymentMethodId,
            ReservedAtUtc = now,
            CorrelationId = correlationId,
            ReservedAtVersion = subscription.Version
        };

        if (!await _subscriptions.TryReserveSettlementAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                reservation,
                cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
        }

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
                "Subscription opening-stub upgrade was not charged TenantHash={TenantHash} " +
                "SubscriptionHash={SubscriptionHash} Kind={Kind} Reason={Reason}",
                PaymentLogValue.Hash(subscription.TenantId),
                PaymentLogValue.Hash(subscription.ItemId),
                charge.FailureKind,
                PaymentLogValue.Label(charge.ErrorCode ?? "unknown"));

            if (!SettledFailureKinds.Contains(charge.FailureKind))
            {
                _logger.LogError(
                    "A subscription opening-stub upgrade left its charge unanswered and is held " +
                    "for reconciliation SubscriptionHash={SubscriptionHash} Kind={Kind}",
                    PaymentLogValue.Hash(subscription.ItemId),
                    charge.FailureKind);

                return Failure(
                    charge.FailureKind,
                    "subscription_plan_change_charge_unresolved",
                    "The charge for this plan change could not be confirmed. " +
                    "Re-read the subscription before trying again.",
                    correlationId);
            }

            await ReleaseAsync(subscription, reservation, cancellationToken);

            return charge.ToFailure<SubscriptionResponse>();
        }

        replacementAnnual.PaymentDetailId = charge.Value;

        return await ApplyAsync(
            subscription, newPlan, newPrice, quantities, compositeSchedule,
            stubUpgrade.NewCreditBalanceMinor, charge.Value, reservation.ReservationId,
            correlationId, cancellationToken,
            reservation.Settlement, requestedByUserId, replacementAnnual);
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
        // the charge, which is the only thing that can tell a lost release from a settled one.
        _logger.LogError(
            "A declined subscription plan change could not release its reservation " +
            "SubscriptionHash={SubscriptionHash}",
            PaymentLogValue.Hash(subscription.ItemId));
    }

    /// <summary>
    /// Holds a plan change until the period the subscriber has already paid for runs out.
    /// </summary>
    /// <remarks>
    /// Charges nothing, refunds nothing and banks nothing. The subscriber keeps the plan they paid
    /// for until <see cref="PendingPlanChange.EffectiveAtUtc"/>, and the renewal at that boundary
    /// installs what is frozen here.
    /// <para>
    /// Everything the boundary needs is frozen now rather than re-resolved then: the plan, the
    /// price, the quantities and both schedules. A renewal a month later that re-read the
    /// catalogue could move the subscriber onto a plan that had been repriced or archived in the
    /// meantime — terms nobody quoted them.
    /// </para>
    /// </remarks>
    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ScheduleAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        DateTime now,
        string? requestedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var effectiveAtUtc = SubscriptionPaidPeriod.PaidThroughUtc(subscription);

        // Derived from the instant this becomes real, never from the instant it was asked for. A
        // change requested on 15 September to take effect on 1 October, onto an anniversary annual
        // price, would otherwise be stored anchored on 15 September — and the renewal that installs
        // it would open a year running from a date the subscriber was never on that plan.
        //
        // The usage schedule matters just as much: anchoring it at request time can invent a
        // metering-window transition that the change itself never called for.
        if (!TryBuildSchedule(subscription, newPlan, newPrice, effectiveAtUtc, out var targetSchedule))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The target plan's schedules could not be derived for the date this change takes "
                    + "effect.",
                correlationId);
        }

        var pending = new PendingPlanChange
        {
            Plan = newPlan,
            Price = newPrice,
            QuantityItems = quantities,
            FeeSchedule = targetSchedule.FeeSchedule,
            UsageSchedule = targetSchedule.UsageSchedule,
            RequestedAtUtc = now,
            EffectiveAtUtc = effectiveAtUtc,
            RequestedByUserId = requestedByUserId,
            ExpectedVersion = subscription.Version
        };

        if (!await _subscriptions.TrySetPendingPlanChangeAsync(
                subscription.TenantId,
                subscription.ItemId,
                subscription.Version,
                pending,
                cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being scheduled.",
                correlationId);
        }

        // Recorded against the subscription as it still is, so the entry names the plan being
        // left as well as the one being moved to.
        await RecordScheduleAuditAsync(
            subscription, "SchedulePlanChange", pending, requestedByUserId, correlationId,
            cancellationToken);

        // Reflected on the copy being mapped back, so the response describes the subscription as it
        // now is rather than as it was read. Nothing else about it moved: the plan, price and
        // quantities in force are still the ones being paid for.
        subscription.PendingPlanChange = pending;
        subscription.Version++;

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        _logger.LogInformation(
            "Subscription plan change scheduled TenantHash={TenantHash} " +
            "SubscriptionHash={SubscriptionHash} CurrentPlan={CurrentPlan} TargetPlan={TargetPlan} " +
            "EffectiveAtUtc={EffectiveAtUtc} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(subscription.Plan.Code),
            PaymentLogValue.Label(newPlan.Code),
            pending.EffectiveAtUtc,
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<SubscriptionResponse>> ApplyAsync(
        SubscriptionDetail subscription,
        PlanSnapshot newPlan,
        PriceSnapshot newPrice,
        List<SubscriptionQuantityItem> quantities,
        SubscriptionPlanSchedule newSchedule,
        long newCreditBalanceMinor,
        string? paymentDetailId,
        string? reservationId,
        string correlationId,
        CancellationToken cancellationToken,
        SubscriptionSettlementBreakdown? settlement = null,
        string? initiatedByUserId = null,
        PendingAnnualPeriod? replacementPendingAnnualPeriod = null)
    {
        var previousPlanCode = subscription.Plan.Code;
        var expectedVersion = subscription.Version;
        var outgoingUsagePeriod = await SnapshotOutgoingUsagePeriodAsync(
            subscription, correlationId, cancellationToken);

        // No credit note, because no plan change banks credit any more. A downgrade used to credit
        // the unused time on the plan being left; it is now scheduled for the end of the period
        // already paid for instead, so the subscriber keeps what they bought and there is no
        // unused time to hand back. An immediate upgrade only ever consumes credit, and consuming
        // it is recorded on the settlement invoice rather than as a document of its own.
        subscription.Plan = newPlan;
        subscription.Price = newPrice;
        subscription.QuantityItems = quantities;
        subscription.FeeSchedule = newSchedule.FeeSchedule;
        subscription.CurrentPeriodStartUtc = newSchedule.CurrentPeriodStartUtc;
        subscription.CurrentPeriodEndUtc = newSchedule.CurrentPeriodEndUtc;
        subscription.NextFeeBillingAtUtc = newSchedule.NextFeeBillingAtUtc;
        subscription.UsageSchedule = newSchedule.UsageSchedule;
        subscription.CurrentUsagePeriodStartUtc = newSchedule.CurrentUsagePeriodStartUtc;
        subscription.CurrentUsagePeriodEndUtc = newSchedule.CurrentUsagePeriodEndUtc;
        subscription.NextUsageBillingAtUtc = newSchedule.NextUsageBillingAtUtc;
        subscription.CreditBalanceMinor = newCreditBalanceMinor;

        // Only an opening-stub upgrade passes this, replacing the prepaid annual period it just
        // settled alongside its stub with the new one priced on the target's terms. Left as-is
        // otherwise: an ordinary plan change touches no annual period at all.
        if (replacementPendingAnnualPeriod is not null)
        {
            subscription.PendingAnnualPeriod = replacementPendingAnnualPeriod;
        }

        var outboxEvent = _events.CreatePlanChanged(
            subscription,
            previousPlanCode,
            correlationId);
        var applied = await _subscriptions.TryChangePlanAsync(
            subscription.TenantId,
            subscription.ItemId,
            expectedVersion,
            reservationId,
            newPlan,
            newPrice,
            quantities,
            newSchedule,
            outgoingUsagePeriod,
            newCreditBalanceMinor,
            paymentDetailId,
            outboxEvent,
            cancellationToken,
            replacementPendingAnnualPeriod: replacementPendingAnnualPeriod);

        if (!applied)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_plan_change_conflict",
                "The subscription changed while this plan change was being applied.",
                correlationId);
        }

        if (_scheduler is not null)
        {
            await _scheduler.ScheduleOutboxPublicationAsync(
                subscription,
                outboxEvent,
                cancellationToken);
        }

        _cache.Invalidate(subscription.TenantId, subscription.OrganizationId);

        await RecordDocumentsAsync(
            subscription,
            paymentDetailId,
            initiatedByUserId,
            correlationId,
            cancellationToken);

        _logger.LogInformation(
            "Subscription plan changed TenantHash={TenantHash} SubscriptionHash={SubscriptionHash} " +
            "PreviousPlan={PreviousPlan} NewPlan={NewPlan} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.TenantId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(previousPlanCode),
            PaymentLogValue.Label(newPlan.Code),
            correlationId);

        return SubscriptionOperationResult<SubscriptionResponse>.Success(
            _mapper.ToResponse(subscription),
            correlationId);
    }

    /// <summary>
    /// Asks for the invoice this change warrants, when it charged anything.
    /// </summary>
    /// <remarks>
    /// An invoice or nothing. A plan change either charges the difference now or is scheduled for
    /// the end of the period already paid for, and a scheduled one moves no money today — so there
    /// is never a second document, and a change that came to nothing at all produces none.
    /// <para>
    /// Only a request. The invoice's obligation is recorded by the announcer, so a failure here
    /// costs a delay rather than a document. Still swallowed: the plan has changed and the money
    /// has moved, and throwing would cost the subscriber the change they paid for.
    /// </para>
    /// </remarks>
    private async Task RecordDocumentsAsync(
        SubscriptionDetail subscription,
        string? paymentDetailId,
        string? initiatedByUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (_announcer is null)
        {
            return;
        }

        try
        {
            if (paymentDetailId is { Length: > 0 } invoiced)
            {
                await _announcer.AnnounceChargeAsync(
                    subscription,
                    invoiced,
                    SubscriptionChargeKind.PlanChange,
                    null,
                    correlationId,
                    cancellationToken,
                    SubscriptionDocumentSourceFactory.ActorOf(initiatedByUserId));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "A plan change completed but its financial document could not be requested " +
                "SubscriptionHash={SubscriptionHash} CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(subscription.ItemId),
                correlationId);
        }
    }

    /// <summary>
    /// What the organization's billing profile still needs, and remembers who is asking when it is
    /// complete.
    /// </summary>
    /// <param name="preview">
    /// True to leave <see cref="ISubscriptionBillingProfileGuard.RememberInitiatorAsync"/> unread.
    /// A preview has not changed anything, and recording an initiator for a quote that may never
    /// be confirmed would misname who actually asked for the change.
    /// </param>
    private async Task<IReadOnlyList<string>> MissingBillingProfileFieldsAsync(
        SubscriptionContext context,
        bool preview,
        CancellationToken cancellationToken)
    {
        if (_billingProfile is null)
        {
            return [];
        }

        var missing = await _billingProfile.MissingFieldsAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (missing.Count == 0 && !preview)
        {
            await _billingProfile.RememberInitiatorAsync(
                context.TenantId,
                context.OrganizationId,
                context.UserId,
                context.UserName,
                context.UserEmail,
                cancellationToken);
        }

        return missing;
    }

    private async Task<SubscriptionOperationResult<(Plan Plan, Price Price)>> ResolveTargetAsync(
        ChangeSubscriptionPlanRequest request,
        SubscriptionContext context,
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var plan = await _catalogue.FindPlanByCodeAsync(
            context.TenantId,
            context.OrganizationId,
            request.PlanCode,
            cancellationToken);

        if (plan is null)
        {
            // Consulted only after the active fallback has found nothing, and only to say why.
            // Resolving both statuses together would let an organization's archived plan shadow
            // the tenant's active one of the same code and refuse a move that should have been
            // allowed — what is sellable must keep being decided by the lookup above alone.
            var archived = await _catalogue.FindArchivedPlanByCodeAsync(
                context.TenantId,
                context.OrganizationId,
                request.PlanCode,
                cancellationToken);

            return archived is null
                ? SubscriptionOperationResult<(Plan, Price)>.Failure(
                    PaymentFailureKind.NotFound,
                    "subscription_plan_not_found",
                    "The plan does not exist or is not on sale.",
                    correlationId)
                : SubscriptionOperationResult<(Plan, Price)>.Failure(
                    PaymentFailureKind.Conflict,
                    "subscription_plan_archived",
                    "This plan is archived and can no longer be sold or changed.",
                    correlationId);
        }

        var price = await _catalogue.GetPriceAsync(
            context.TenantId,
            request.PriceId,
            cancellationToken);

        if (price is null ||
            !string.Equals(price.PlanId, plan.ItemId, StringComparison.Ordinal) ||
            price.Status != CatalogueStatus.Active)
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_price_not_found",
                "The price does not exist for this plan.",
                correlationId);
        }

        if (!string.Equals(price.CurrencyCode, subscription.CurrencyCode, StringComparison.Ordinal))
        {
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.Validation,
                "subscription_plan_change_currency_mismatch",
                "A subscription cannot change to a price in a different currency.",
                correlationId);
        }

        return SubscriptionOperationResult<(Plan, Price)>.Success((plan, price), correlationId);
    }

    private static bool TryBuildSchedule(
        SubscriptionDetail subscription,
        PlanSnapshot targetPlan,
        PriceSnapshot targetPrice,
        DateTime now,
        out SubscriptionPlanSchedule schedule)
    {
        schedule = null!;
        var timeZoneId = subscription.FeeSchedule.TimeZoneId;

        // Moving onto a calendar-aligned price installs that price's own boundaries, exactly as
        // subscribing to it would. The subscriber is not left on their old anniversary until some
        // later renewal notices — the schedule they move onto is the one they were shown.
        var calendarAligned = CalendarBillingAlignment.IsCalendarAligned(targetPrice);

        var feeBuilt = calendarAligned
            ? CalendarBillingAlignment.TryCreateSchedule(
                targetPrice.Interval, now, timeZoneId, out var fee)
            : BillingPeriodCalculator.TryCreateSchedule(
                targetPrice.Interval, targetPrice.IntervalCount, now, timeZoneId, out fee);

        if (!feeBuilt ||
            !BillingPeriodCalculator.TryGetPeriod(fee, now, out var feePeriod) ||
            !BillingPeriodCalculator.TryCreateSchedule(
                targetPlan.UsageInterval, targetPlan.UsageIntervalCount, now, timeZoneId, out var usage) ||
            !BillingPeriodCalculator.TryGetPeriod(usage, now, out var usagePeriod))
        {
            return false;
        }

        var fraction = default(BillingDayFraction);
        var feeStartUtc = feePeriod.StartUtc;
        var feeEndUtc = feePeriod.EndUtc;

        if (calendarAligned)
        {
            if (!CalendarBillingAlignment.TryResolveFirstPeriod(now, timeZoneId, out var first))
            {
                return false;
            }

            // Only a stub replaces the derived period; a change landing on the first opens a
            // whole period at the target's own cadence, which the schedule already derived.
            if (first.IsProrated)
            {
                feeStartUtc = first.StartUtc;
                feeEndUtc = first.EndUtc;
            }

            fraction = BillingDayFraction.Of(first);
        }

        schedule = new SubscriptionPlanSchedule(
            fee,
            feeStartUtc,
            feeEndUtc,
            feeEndUtc,
            usage,
            usagePeriod.StartUtc,
            usagePeriod.EndUtc,
            usagePeriod.EndUtc,
            fraction);
        return true;
    }

    private async Task<PendingUsagePeriod> SnapshotOutgoingUsagePeriodAsync(
        SubscriptionDetail subscription,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var periodKey = PeriodKey.Create(
            subscription.UsageSchedule.Interval,
            subscription.CurrentUsagePeriodStartUtc);

        return new PendingUsagePeriod
        {
            PeriodKey = periodKey,
            PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
            PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
            Plan = subscription.Plan,
            Price = subscription.Price,
            CurrencyCode = subscription.CurrencyCode,
            CorrelationId = correlationId,
            // Snapshotted here, before the schedule swap re-anchors UsageSchedule — a
            // carry-forward meter's actual carried-in allowance for this outgoing window would
            // otherwise be lost once the new schedule is installed. Null (falling back to live
            // resolution at rating time) when the resolver/repository were not supplied.
            MeterAllowances = await MeterAllowanceSnapshot.CaptureAsync(
                subscription,
                new BillingPeriod(
                    0,
                    subscription.CurrentUsagePeriodStartUtc,
                    subscription.CurrentUsagePeriodEndUtc,
                    periodKey),
                _usage,
                _allowances,
                cancellationToken)
        };
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

    private static SubscriptionOperationResult<TValue> Failure<TValue>(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<TValue>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId);

    /// <summary>
    /// The two priced sides of a settlement, in the shape a client already knows how to render —
    /// the same one an issued document's settlement carries.
    /// </summary>
    /// <remarks>
    /// Converts directly from the calculator's own <see cref="ProrationBreakdown"/> rather than
    /// from the persisted <see cref="Payment.DomainService.Entities.SubscriptionSettlementBreakdown"/>
    /// that <see cref="SettlementCharge.BreakdownOf"/> builds for a real reservation — a preview
    /// persists nothing, so there is no reservation to convert from.
    /// </remarks>
    private static FinancialDocumentSettlementResponse SettlementResponseOf(ProrationBreakdown breakdown) =>
        new()
        {
            Outgoing = SettlementSideResponseOf(breakdown.Outgoing),
            Target = SettlementSideResponseOf(breakdown.Target),
            CreditConsumedMinor = breakdown.CreditConsumedMinor,
            NetSettlementMinor = breakdown.NetSettlementMinor
        };

    private static FinancialDocumentSettlementSideResponse SettlementSideResponseOf(ProrationSide side) =>
        new()
        {
            GrossAmountMinor = side.GrossAmountMinor,
            BuiltInDiscountMinor = side.BuiltInDiscountMinor,
            PromotionalDiscountMinor = side.PromotionalDiscountMinor,
            TaxAmountMinor = side.TaxAmountMinor,
            PeriodTotalMinor = side.PeriodTotalMinor,
            ProratedValueMinor = side.ProratedValueMinor
        };

    /// <summary>
    /// What <see cref="ResolveAsync"/> resolved, before either caller decides what to do with it:
    /// price it, or spend it.
    /// </summary>
    private readonly record struct PlanChangeResolution(
        SubscriptionDetail Subscription,
        PlanSnapshot NewPlan,
        PriceSnapshot NewPrice,
        List<SubscriptionQuantityItem> Quantities,
        SubscriptionPlanSchedule NewSchedule,
        DateTime Now,
        string? RequestedByUserId,
        List<SubscriptionPreviewBlockerResponse> Blockers);
}
