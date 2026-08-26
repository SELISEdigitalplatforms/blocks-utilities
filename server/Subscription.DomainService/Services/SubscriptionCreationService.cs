using FluentValidation;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns a chosen plan and price into a stored subscription.
/// </summary>
public sealed class SubscriptionCreationService : ISubscriptionCreationService
{
    private const string StripeProvider = PaymentConstants.StripeProvider;

    private readonly ISubscriptionCatalogueRepository _catalogue;
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionDiscountRepository _discounts;
    private readonly IBillingAccountRepository _billingAccounts;
    private readonly IValidator<CreateSubscriptionRequest> _validator;
    private readonly ILogger<SubscriptionCreationService> _logger;
    private readonly ISubscriptionWorkScheduler? _scheduler;
    private readonly TimeProvider _time;

    public SubscriptionCreationService(
        ISubscriptionCatalogueRepository catalogue,
        ISubscriptionRepository subscriptions,
        ISubscriptionDiscountRepository discounts,
        IBillingAccountRepository billingAccounts,
        IValidator<CreateSubscriptionRequest> validator,
        ILogger<SubscriptionCreationService> logger,
        TimeProvider? time = null,
        ISubscriptionWorkScheduler? scheduler = null,
        ISubscriptionBillingProfileGuard? billingProfile = null)
    {
        _catalogue = catalogue;
        _subscriptions = subscriptions;
        _discounts = discounts;
        _billingAccounts = billingAccounts;
        _validator = validator;
        _logger = logger;
        _scheduler = scheduler;
        _billingProfile = billingProfile;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether there is anybody to address this organization's invoices to.
    /// </summary>
    /// <remarks>
    /// Optional so existing callers compile unchanged; where it is absent the requirement is simply
    /// not enforced, which is the same outcome as turning it off in configuration.
    /// </remarks>
    private readonly ISubscriptionBillingProfileGuard? _billingProfile;

    public async Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var invalid = await SubscriptionValidation
            .CheckAsync<CreateSubscriptionRequest, SubscriptionDetail>(
                _validator,
                request,
                "subscription_request_invalid",
                "The subscription request is invalid.",
                correlationId,
                cancellationToken);

        if (invalid is not null)
        {
            return invalid;
        }

        var terms = await ResolveTermsAsync(request, context, correlationId, cancellationToken);

        if (!terms.IsSuccess)
        {
            return terms.ToFailure<SubscriptionDetail>();
        }

        var (plan, price) = terms.Value;

        var profileMissing = await MissingBillingProfileFieldsAsync(
            context,
            plan,
            price,
            cancellationToken);

        if (profileMissing.Count > 0)
        {
            // Refused before anything is charged, which is the only moment refusing is free. Once the
            // money has moved the invoice is owed whatever the profile says, and it will be addressed
            // to an organization id — see the issuer's subscriber snapshot.
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_billing_profile_incomplete",
                "This organization's billing profile is missing details an invoice must carry. " +
                    "Complete it before starting a paid subscription.",
                correlationId,
                new Dictionary<string, string[]>
                {
                    ["BillingProfile"] = [.. profileMissing]
                });
        }

        var discount = await ResolveDiscountAsync(request, context, plan, price, correlationId, cancellationToken);
        if (!discount.IsSuccess) return discount.ToFailure<SubscriptionDetail>();
        var quantities = SubscriptionQuantityBuilder.Build(request.Quantities, plan, price);

        if (quantities is null)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_quantity_invalid",
                "The quantities do not match the plan's items or fall outside their bounds.",
                correlationId);
        }

        var now = _time.GetUtcNow().UtcDateTime;

        if (!BillingLocalTime.TryFindTimeZone(request.TimeZoneId, out var timeZone))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The billing schedule could not be derived from this time zone.",
                correlationId);
        }

        // Resolved once, here, and reused for both the schedule anchor below and the TrialTerms
        // frozen onto the subscription — a trial's boundary must never be computed twice and risk
        // disagreeing with itself.
        var trial = BuildTrial(plan, now, timeZone);

        // A calendar-aligned price anchors on the first of the month rather than on this instant;
        // every later boundary then derives from that anchor exactly as an anniversary one does.
        // The usage schedule is never realigned — metering keeps the plan's own independent
        // cadence, which is the whole reason it is a separate schedule.
        var calendarAligned = CalendarBillingAlignment.IsCalendarAligned(
            price.BillingAlignment,
            price.Interval,
            price.IntervalCount,
            price.CalendarStubBaseUnitAmountMinor);

        // A card-free trial defers the first paid period to the day it ends, and for a yearly
        // price that day decides the whole annual cycle: a 25 August signup on a trial running to
        // 20 September starts its year on 1 October, not 1 September. The trial's end is known
        // here, so the schedule is anchored on it rather than corrected later — every boundary
        // derives from the anchor, and one anchored a month early stays a month early forever.
        var scheduleAnchorUtc = trial is not null && !plan.TrialRequiresPaymentMethod
            ? trial.EndsAtUtc
            : now;

        var feeScheduleBuilt = calendarAligned
            ? CalendarBillingAlignment.TryCreateSchedule(
                price.Interval, scheduleAnchorUtc, request.TimeZoneId, out var feeSchedule)
            : BillingPeriodCalculator.TryCreateSchedule(
                price.Interval,
                price.IntervalCount,
                scheduleAnchorUtc,
                request.TimeZoneId,
                out feeSchedule);

        if (!feeScheduleBuilt ||
            !BillingPeriodCalculator.TryCreateSchedule(
                plan.UsageInterval,
                plan.UsageIntervalCount,
                now,
                request.TimeZoneId,
                out var usageSchedule))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The billing schedule could not be derived from this time zone.",
                correlationId);
        }

        var account = await _billingAccounts.GetOrCreateAsync(
            new BillingAccount
            {
                TenantId = context.TenantId,
                OrganizationId = context.OrganizationId,
                ProviderName = StripeProvider,
                BillingEmail = request.BillingEmail,
                BillingName = request.BillingName
            },
            cancellationToken);

        var subscription = BuildSubscription(
            context,
            plan,
            price,
            quantities,
            account,
            feeSchedule,
            usageSchedule,
            discount.Value,
            trial,
            now,
            correlationId);

        if (!ApplyPeriods(subscription, now, calendarAligned))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_schedule_invalid",
                "The billing periods could not be computed for this schedule.",
                correlationId);
        }

        if (!await _subscriptions.TryCreateAsync(subscription, cancellationToken))
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_already_active",
                "This organization already has a live subscription.",
                correlationId);
        }

        _logger.LogInformation(
            "Subscription created TenantHash={TenantHash} OrganizationHash={OrganizationHash} " +
            "SubscriptionHash={SubscriptionHash} Plan={Plan} Status={Status} CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(context.TenantId),
            PaymentLogValue.Hash(context.OrganizationId),
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Label(plan.Code),
            PaymentLogValue.Label(subscription.Status.ToString()),
            correlationId);

        LogDiscountsApplied(subscription, correlationId);

        // A first charge that is never paid has to be noticed by something. Announced here rather
        // than discovered by a roster pass, and best effort inside the scheduler: a subscription
        // that exists must not be reported as failed because its recovery could not be booked.
        if (_scheduler is not null && subscription.Status == SubscriptionStatus.Incomplete)
        {
            await _scheduler.ScheduleActivationRecoveryAsync(subscription, cancellationToken);
        }

        return SubscriptionOperationResult<SubscriptionDetail>.Success(
            subscription,
            correlationId);
    }

    private async Task<SubscriptionOperationResult<(Plan Plan, Price Price)>> ResolveTermsAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
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
            return SubscriptionOperationResult<(Plan, Price)>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_plan_not_found",
                "The plan does not exist or is not on sale.",
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

        return SubscriptionOperationResult<(Plan, Price)>.Success(
            (plan, price),
            correlationId);
    }

    private async Task<SubscriptionOperationResult<DiscountTerms?>> ResolveDiscountAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        Plan plan,
        Price price,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DiscountCode))
            return SubscriptionOperationResult<DiscountTerms?>.Success(null, correlationId);

        var discount = await _discounts.FindActiveByCodeAsync(
            context.TenantId,
            context.OrganizationId,
            request.DiscountCode.Trim().ToLowerInvariant(),
            cancellationToken);

        if (discount is null)
            return SubscriptionOperationResult<DiscountTerms?>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_discount_not_found",
                "The discount code does not exist or is retired.",
                correlationId);

        if (discount.Terms.ExpiresAtUtc is { } expiry && expiry <= _time.GetUtcNow().UtcDateTime)
            return SubscriptionOperationResult<DiscountTerms?>.Failure(
                PaymentFailureKind.Validation,
                "subscription_discount_expired",
                "The discount code has expired.",
                correlationId);

        // One rule, shared with the plan-change path, so a restriction cannot be enforced at signup
        // and forgotten the first time the subscriber moves.
        if (!SubscriptionDiscountApplicability.Permits(discount, plan.Code, price.ItemId))
            return SubscriptionOperationResult<DiscountTerms?>.Failure(
                PaymentFailureKind.Validation,
                "subscription_discount_not_applicable",
                "The discount does not apply to this plan and price.",
                correlationId);

        if (discount.Terms.Kind == DiscountKind.FixedAmount &&
            !string.Equals(discount.CurrencyCode, price.CurrencyCode, StringComparison.Ordinal))
            return SubscriptionOperationResult<DiscountTerms?>.Failure(
                PaymentFailureKind.Validation,
                "subscription_discount_currency_mismatch",
                "The fixed discount is denominated in another currency.",
                correlationId);

        var terms = discount.Terms;
        return SubscriptionOperationResult<DiscountTerms?>.Success(
            new DiscountTerms
            {
                Code = terms.Code,
                Kind = terms.Kind,
                PercentBasisPoints = terms.PercentBasisPoints,
                AmountMinor = terms.AmountMinor,
                DurationPeriods = terms.DurationPeriods,
                ExpiresAtUtc = terms.ExpiresAtUtc,
                // Copied so the restriction outlives the redemption. A plan change re-asks the same
                // question, and it can only do so against terms that remember the answer.
                ApplicablePlanCodes = [.. discount.ApplicablePlanCodes],
                ApplicablePriceIds = [.. discount.ApplicablePriceIds]
            },
            correlationId);
    }

    /// <summary>
    /// What reduced this subscription's charge, and where each reduction came from.
    /// </summary>
    /// <remarks>
    /// Money that came off has to be explainable months later, by which time the catalogue will have
    /// moved and the price may not even be on sale. Written once at signup, against the snapshot the
    /// subscription actually holds, so the record cannot disagree with what is charged. Nothing
    /// customer-identifying: the plan and the price are hashed exactly as they are everywhere else
    /// in this module, and the numbers are terms rather than personal data.
    /// </remarks>
    private void LogDiscountsApplied(SubscriptionDetail subscription, string correlationId)
    {
        var charge = SubscriptionAmountCalculator.PeriodAmountMinor(
            subscription,
            _time.GetUtcNow().UtcDateTime);

        if (charge.BuiltInDiscountMinor == 0 &&
            charge.PromotionalDiscountMinor == 0 &&
            subscription.Price.AutomaticDiscountBasisPoints is null or 0)
        {
            // Nothing came off, so there is nothing to explain. A line here would be noise on the
            // overwhelming majority of subscriptions.
            return;
        }

        _logger.LogInformation(
            "Subscription discounts applied SubscriptionHash={SubscriptionHash} "
                + "PriceHash={PriceHash} AutomaticBasisPoints={AutomaticBasisPoints} "
                + "Combination={Combination} PromotionPolicy={PromotionPolicy} "
                + "PromotionCode={PromotionCode} GrossMinor={GrossMinor} "
                + "BuiltInDiscountMinor={BuiltInDiscountMinor} "
                + "PromotionalDiscountMinor={PromotionalDiscountMinor} "
                + "CorrelationId={CorrelationId}",
            PaymentLogValue.Hash(subscription.ItemId),
            PaymentLogValue.Hash(subscription.Price.PriceId),
            subscription.Price.AutomaticDiscountBasisPoints ?? 0,
            PaymentLogValue.Label(
                SubscriptionDiscountPresentation.Describe(subscription.Price) ?? "None"),
            PaymentLogValue.Label(
                subscription.Plan.QuantityDiscountCombinationPolicy.ToString()),
            PaymentLogValue.Label(subscription.Discount?.Code ?? "None"),
            charge.GrossAmountMinor,
            charge.BuiltInDiscountMinor,
            charge.PromotionalDiscountMinor,
            correlationId);
    }

    private static SubscriptionDetail BuildSubscription(
        SubscriptionContext context,
        Plan plan,
        Price price,
        List<SubscriptionQuantityItem> quantities,
        BillingAccount account,
        BillingSchedule feeSchedule,
        BillingSchedule usageSchedule,
        DiscountTerms? discount,
        TrialTerms? trial,
        DateTime now,
        string correlationId)
    {
        var subscriptionId = Guid.NewGuid().ToString();

        return new SubscriptionDetail
        {
            ItemId = subscriptionId,
            TenantId = context.TenantId,
            OrganizationId = context.OrganizationId,
            BillingAccountId = account.ItemId,
            Status = SubscriptionStatus.Incomplete,
            CurrencyCode = price.CurrencyCode,
            Plan = SubscriptionSnapshotBuilder.SnapshotOf(plan),
            Price = SubscriptionSnapshotBuilder.SnapshotOf(price),
            QuantityItems = quantities,
            FeeSchedule = feeSchedule,
            UsageSchedule = usageSchedule,
            Discount = discount,
            Trial = trial,
            OrderId = SubscriptionConstants.OrderIdFor(subscriptionId),
            CorrelationId = correlationId,
            CreatedAtUtc = now,
            LastUpdatedDateUtc = now
        };
    }

    /// <summary>
    /// Resolves this plan's trial, in the subscription's own time zone, into the frozen terms a
    /// later catalogue edit can no longer move.
    /// </summary>
    private static TrialTerms? BuildTrial(Plan plan, DateTime now, TimeZoneInfo timeZone)
    {
        if (!TrialDurationNormalizer.HasTrial(plan))
        {
            return null;
        }

        var count = TrialDurationNormalizer.EffectiveCount(plan);
        var endsAtUtc = TrialDurationResolver.ResolveEndUtc(now, timeZone, plan.TrialDurationKind, count);

        return new TrialTerms
        {
            StartsAtUtc = now,
            EndsAtUtc = endsAtUtc,
            DurationKind = plan.TrialDurationKind,
            DurationCount = count,
            RequiresPaymentMethod = plan.TrialRequiresPaymentMethod,
            Grants = plan.TrialGrants
                .Select(grant => new TrialMeterGrant
                {
                    MeterKey = grant.MeterKey,
                    IncludedQuantity = grant.IncludedQuantity
                })
                .ToList()
        };
    }

    private static bool ApplyPeriods(
        SubscriptionDetail subscription,
        DateTime now,
        bool calendarAligned)
    {
        if (!BillingPeriodCalculator.TryGetPeriod(
                subscription.FeeSchedule,
                now,
                out var feePeriod) ||
            !BillingPeriodCalculator.TryGetPeriod(
                subscription.UsageSchedule,
                now,
                out var usagePeriod))
        {
            return false;
        }

        var fraction = default(BillingDayFraction);

        if (calendarAligned)
        {
            if (!CalendarBillingAlignment.TryResolveFirstPeriod(
                    now,
                    subscription.FeeSchedule.TimeZoneId,
                    out var first))
            {
                return false;
            }

            // The stub, not the whole period the schedule derives. A subscriber joining on the
            // 25th is entitled from the 25th and pays from the 25th; the derived period starting
            // on the 1st is time they were not here for.
            //
            // Only when there *is* a stub. A signup on the local first opens a whole period at the
            // price's own cadence — a month for a monthly price, a year for a yearly one — and the
            // derived period already says so. Overriding it here would cut an annual subscription
            // down to its first month.
            if (first.IsProrated)
            {
                feePeriod = feePeriod with
                {
                    StartUtc = first.StartUtc,
                    EndUtc = first.EndUtc
                };
            }

            fraction = BillingDayFraction.Of(first);
        }

        // A calendar-aligned yearly subscription that opens mid-month has bought two things: the
        // stub, and the year that starts on the first. The year is priced and frozen here even
        // though it may not be collected until its boundary — what the subscriber was quoted is
        // what settles, whichever of the two timings the price is on.
        //
        // A card-free trial is the exception, for the same reason its stub is: which month the
        // trial ends in decides both. A signup on 25 August whose trial runs to 20 September owes
        // a 20–30 September stub and a year starting 1 October, and a year frozen today would say
        // 1 September. It is priced at conversion instead, atomically with the stub it follows.
        var annual = fraction.IsPartial && subscription.Trial is not { RequiresPaymentMethod: false }
            ? BuildPendingAnnualPeriod(subscription, feePeriod.EndUtc, now)
            : null;

        subscription.PendingAnnualPeriod = annual;

        // A card-free trial charges nothing now, and what its first paid period will cost depends
        // on when the trial ends — a date that is not this one. Every initial-charge field is left
        // unset rather than filled in from today, because a stored 26/31 that the eventual charge
        // contradicts is worse than an absent one: it reads as a charge that was made.
        //
        // Everything else freezes here, while the terms are the ones the customer is being quoted.
        // A checkout paid tomorrow, resumed next week, or recovered by a sweep settles these
        // figures and not freshly derived ones — a stub priced by the day would otherwise shrink
        // underneath a customer who left the page open overnight.
        if (subscription.Trial is not { RequiresPaymentMethod: false })
        {
            // A promotional code belongs to the year, not to the days before it — so a yearly stub
            // is priced without one. Its automatic discount and volume band still apply, because
            // those are properties of the price rather than something the customer spends.
            var charge = SubscriptionAmountCalculator.FirstPeriodCharge(
                subscription,
                fraction,
                now,
                includePromotionalDiscount: annual is null);

            subscription.InitialChargeAmountMinor = annual is { CollectedWithCheckout: true }
                ? charge.AmountMinor + annual.AmountMinor
                : charge.AmountMinor;
            subscription.InitialChargeDiscountApplied =
                charge.DiscountApplied ||
                annual is { CollectedWithCheckout: true, DiscountApplied: true };
            subscription.InitialChargeProrated = fraction.IsPartial;
            subscription.ProrationDays = fraction.IsPartial ? fraction.CoveredDays : null;
            subscription.ProrationTotalDays = fraction.IsPartial ? fraction.TotalDays : null;
        }

        subscription.CurrentPeriodStartUtc = feePeriod.StartUtc;
        subscription.CurrentPeriodEndUtc = feePeriod.EndUtc;

        // Only a card-free trial defers the first fee to the day it ends, because only that
        // one starts without taking payment. A trial that demands a card is charged for its
        // first period up front — the money path has no way to hold a card without charging it
        // — so that period is already paid, and billing again on the trial's last day would
        // take the same money twice. This condition deliberately mirrors the one
        // SubscriptionCheckoutService uses to decide whether to charge at all.
        subscription.NextFeeBillingAtUtc =
            subscription.Trial is { RequiresPaymentMethod: false } trial
                ? trial.EndsAtUtc
                : feePeriod.EndUtc;
        subscription.CurrentUsagePeriodStartUtc = usagePeriod.StartUtc;
        subscription.CurrentUsagePeriodEndUtc = usagePeriod.EndUtc;
        subscription.NextUsageBillingAtUtc = usagePeriod.EndUtc;

        return true;
    }

    /// <summary>
    /// The year a mid-month calendar-aligned yearly signup has bought but not yet started.
    /// </summary>
    /// <remarks>
    /// Null for everything else, including a monthly calendar price — whose stub is followed by
    /// another month of the same price, not by a separate term that has to be remembered. Shared
    /// with the renewal service, which builds the same record when a card-free trial converts.
    /// <para>
    /// Priced at the full annual amount with the subscriber's promotional code applied, because the
    /// code applies to the year. Frozen here and never recalculated: the boundary is a month away,
    /// and a charge that re-derived its own amount could take a different sum than the one quoted.
    /// </para>
    /// </remarks>
    internal static PendingAnnualPeriod? BuildPendingAnnualPeriod(
        SubscriptionDetail subscription,
        DateTime annualStartUtc,
        DateTime now)
    {
        if (!CalendarBillingAlignment.IsCalendarAligned(subscription.Price) ||
            !CalendarBillingAlignment.NeedsStubBasePrice(
                subscription.Price.Interval,
                subscription.Price.IntervalCount) ||
            !BillingPeriodCalculator.TryGetPeriod(
                subscription.FeeSchedule,
                annualStartUtc,
                out var annualPeriod))
        {
            return null;
        }

        // The whole year, undiminished by any day fraction — the stub covered the days before it.
        var charge = SubscriptionAmountCalculator.FirstPeriodCharge(subscription, default, now);

        return new PendingAnnualPeriod
        {
            StartUtc = annualPeriod.StartUtc,
            EndUtc = annualPeriod.EndUtc,
            AmountMinor = charge.AmountMinor,
            NetAmountMinor = charge.NetAmountMinor,
            TaxAmountMinor = charge.TaxAmountMinor,
            GrossAmountMinor = charge.GrossAmountMinor,
            BuiltInDiscountMinor = charge.BuiltInDiscountMinor,
            PromotionalDiscountMinor = charge.PromotionalDiscountMinor,
            DiscountApplied = charge.DiscountApplied,
            // What the price says to bill for. Whether the money arrived is a separate question,
            // answered by the activation that records the opening payment — a checkout nobody pays
            // must not leave behind a year that reports itself as settled.
            CollectedWithCheckout = subscription.Price.CalendarAnnualChargeTiming ==
                CalendarAnnualChargeTiming.AtCheckout
        };
    }

    private static SubscriptionOperationResult<SubscriptionDetail> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId,
        IReadOnlyDictionary<string, string[]>? validationErrors = null) =>
        SubscriptionOperationResult<SubscriptionDetail>.Failure(
            kind,
            errorCode,
            errorMessage,
            correlationId,
            validationErrors);

    /// <summary>
    /// What the organization's billing profile still needs before it can be invoiced.
    /// </summary>
    /// <remarks>
    /// Asked only of a subscription that will move money. A free plan produces no invoice, so
    /// requiring an invoicing identity to start one would be a rule with no document behind it.
    /// <para>
    /// "Will move money" is read from the price rather than from the computed total, and deliberately
    /// errs towards asking: a plan with quantity items counts even before the quantities are known,
    /// because a subscription that starts free on zero seats will be charged the moment one is added,
    /// and asking then means asking in the middle of an upgrade.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> MissingBillingProfileFieldsAsync(
        SubscriptionContext context,
        Plan plan,
        Price price,
        CancellationToken cancellationToken)
    {
        if (_billingProfile is null ||
            (price.UnitAmountMinor <= 0 && plan.QuantityItems.Count == 0))
        {
            return [];
        }

        var missing = await _billingProfile.MissingFieldsAsync(
            context.TenantId,
            context.OrganizationId,
            cancellationToken);

        if (missing.Count == 0)
        {
            // Whoever starts a subscription is, by acting, somebody an invoice may have to name.
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
}
