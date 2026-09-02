using FluentValidation;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Estimates the cost of additional metered usage using the active subscription's own
/// snapshotted terms, and the same rating, discount and tax logic
/// <see cref="Outbox.SubscriptionUsageRatingProcessor"/> uses to charge the final usage invoice.
/// </summary>
/// <remarks>
/// Deliberately read-only. No usage record, counter update, invoice, payment, outbox event or
/// audit event is ever written from here — this only takes dependencies capable of reading, so
/// there is nothing here that could write even by mistake.
/// <para>
/// Reads exclusively from the subscription's own snapshot — <see cref="SubscriptionDetail.Plan"/>
/// and <see cref="SubscriptionDetail.Price"/> — never from the mutable plan catalogue. An edit to
/// the catalogue after this subscription was sold must not change what this preview reports, for
/// the same reason it must not change what period-end rating eventually charges.
/// </para>
/// </remarks>
public sealed class SubscriptionUsageOveragePreviewService : ISubscriptionUsageOveragePreviewService
{
    private readonly ISubscriptionRepository _subscriptions;
    private readonly ISubscriptionUsageRepository _usage;
    private readonly IMeterAllowanceResolver _allowances;
    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IValidator<PreviewUsageOverageRequest> _validator;
    private readonly TimeProvider _time;

    public SubscriptionUsageOveragePreviewService(
        ISubscriptionRepository subscriptions,
        ISubscriptionUsageRepository usage,
        IMeterAllowanceResolver allowances,
        ISubscriptionContextResolver contextResolver,
        IValidator<PreviewUsageOverageRequest> validator,
        TimeProvider? time = null)
    {
        _subscriptions = subscriptions;
        _usage = usage;
        _allowances = allowances;
        _contextResolver = contextResolver;
        _validator = validator;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>> PreviewAsync(
        PreviewUsageOverageRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = await SubscriptionValidation.CheckAsync<
            PreviewUsageOverageRequest, SubscriptionUsageOveragePreviewResponse>(
            _validator,
            request,
            "subscription_usage_preview_invalid",
            "The overage preview request is invalid.",
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
            return resolution.ToFailure<SubscriptionUsageOveragePreviewResponse>(correlationId);
        }

        var context = resolution.Context!;
        var now = _time.GetUtcNow().UtcDateTime;

        var subscription = await _subscriptions.GetLiveAsync(
            context.TenantId,
            context.OrganizationId,
            now,
            cancellationToken);

        if (subscription is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_not_found",
                "This organization has no active subscription.",
                correlationId);
        }

        var meter = subscription.Plan.Meters.Find(candidate =>
            string.Equals(candidate.MeterKey, request.MeterKey, StringComparison.Ordinal));

        if (meter is null)
        {
            return Failure(
                PaymentFailureKind.NotFound,
                "subscription_meter_not_found",
                "The plan does not define this meter.",
                correlationId);
        }

        if (!MeterPeriodResolver.TryGetPeriod(subscription, meter, now, out var period))
        {
            return Failure(
                PaymentFailureKind.Unavailable,
                "subscription_schedule_unavailable",
                "The usage period could not be determined.",
                correlationId);
        }

        // The same granularity gate usage recording applies, so a preview cannot quote a quantity
        // the subsequent recording would refuse.
        if (!MeterQuantity.IsWithinScale(request.AdditionalQuantity, meter.QuantityScale) ||
            !MeterQuantity.IsWithinMagnitude(request.AdditionalQuantity))
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_quantity_scale_invalid",
                meter.QuantityScale == 0
                    ? "This meter counts whole units."
                    : $"This meter counts to {meter.QuantityScale} decimal places.",
                correlationId);
        }

        if (!meter.OverageAllowed)
        {
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_meter_overage_not_allowed",
                "This meter does not allow usage beyond its included quantity.",
                correlationId);
        }

        var hasRateTable = meter.RateTables.Exists(table => string.Equals(
            table.CurrencyCode, subscription.CurrencyCode, StringComparison.OrdinalIgnoreCase));

        if (!hasRateTable)
        {
            // A silent zero would misreport a plan-authoring gap as "no charge" — the one place
            // this preview refuses outright rather than reusing period-end rating's own tolerant
            // fallback, since a hypothetical quote has no other charge to fall back to.
            return Failure(
                PaymentFailureKind.Conflict,
                "subscription_meter_rate_unavailable",
                "No overage rate is configured for this subscription's currency.",
                correlationId);
        }

        var counter = await _usage.GetCounterAsync(
            context.TenantId,
            SubscriptionUsageCounter.CreateId(subscription.ItemId, meter.MeterKey, period.Key),
            cancellationToken);

        // The append-only ledger is authoritative; the counter is only a fallback for a legacy
        // period whose ledger read returns nothing — the same precedence period-end rating uses
        // (see SubscriptionUsageRatingProcessor.EnsureInvoiceAsync).
        var ledger = await _usage.SummariseLedgerAsync(
            context.TenantId,
            subscription.ItemId,
            meter.MeterKey,
            period.Key,
            cancellationToken);
        var currentUsage = ledger.RecordCount > 0 ? ledger.Balance : counter?.Balance ?? 0;

        var allowance = await _allowances.EffectiveAsync(
            subscription, meter, period, counter, cancellationToken);

        decimal projectedUsage;

        try
        {
            // A valid positive request.AdditionalQuantity added to a very large existing balance
            // could otherwise exceed what a decimal can hold and price a nonsensical "additional"
            // charge. Decimal addition raises rather than wrapping, so this is caught rather than
            // guarded — both operands individually pass validation, so neither is enough to trust
            // the sum.
            projectedUsage = currentUsage + request.AdditionalQuantity;
        }
        catch (OverflowException)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_preview_invalid",
                "The projected usage is outside the range this preview can compute.",
                correlationId);
        }

        var currentOverageUnits = Math.Max(0, currentUsage - allowance);
        var projectedOverageUnits = Math.Max(0, projectedUsage - allowance);

        UsageTierAllocationResult currentAllocations;
        UsageTierAllocationResult projectedAllocations;
        UsageTierAllocationResult additionalAllocations;
        long currentAggregateGross;
        long projectedAggregateGross;

        try
        {
            currentAllocations = SubscriptionUsageRater.OverageAllocations(
                meter, currentOverageUnits, subscription.CurrencyCode);
            projectedAllocations = SubscriptionUsageRater.OverageAllocations(
                meter, projectedOverageUnits, subscription.CurrencyCode);
            additionalAllocations = SubscriptionUsageRater.OverageAllocations(
                meter,
                projectedOverageUnits,
                subscription.CurrencyCode,
                fromOverageUnitsExclusive: currentOverageUnits);

            // The worker totals overage across every billable meter before it ever discounts or
            // taxes anything — see SubscriptionUsageRatingProcessor.EnsureInvoiceAsync. A preview
            // that discounted and taxed only the requested meter in isolation could disagree with
            // the eventual invoice at a rounding boundary whenever another meter also carries
            // overage. Read every other billable meter's *current* contribution so the aggregate
            // this preview discounts and taxes is the same aggregate the invoice will be.
            //
            // Summed with checked arithmetic throughout: a technically valid but very large
            // quantity or unit rate can make an individual meter's tier total, and then this
            // running aggregate, wrap a plain long — silently mispricing the preview instead of
            // refusing it.
            var otherMetersAggregateGross = 0L;

            foreach (var otherMeter in subscription.Plan.Meters)
            {
                if (otherMeter.ResetPolicy == MeterResetPolicy.Never ||
                    string.Equals(otherMeter.MeterKey, meter.MeterKey, StringComparison.Ordinal))
                {
                    continue;
                }

                // Every non-Never meter shares the subscription's own usage schedule, so the
                // period already resolved for the requested meter applies here too — no second
                // resolution.
                var otherCounter = await _usage.GetCounterAsync(
                    context.TenantId,
                    SubscriptionUsageCounter.CreateId(subscription.ItemId, otherMeter.MeterKey, period.Key),
                    cancellationToken);
                var otherLedger = await _usage.SummariseLedgerAsync(
                    context.TenantId,
                    subscription.ItemId,
                    otherMeter.MeterKey,
                    period.Key,
                    cancellationToken);
                var otherUsage = otherLedger.RecordCount > 0
                    ? otherLedger.Balance
                    : otherCounter?.Balance ?? 0;
                var otherAllowance = await _allowances.EffectiveAsync(
                    subscription, otherMeter, period, otherCounter, cancellationToken);
                var otherOverageUnits = Math.Max(0, otherUsage - otherAllowance);

                var otherGross = SubscriptionUsageRater.OverageAllocations(
                    otherMeter, otherOverageUnits, subscription.CurrencyCode).TotalAmountMinor;

                checked
                {
                    otherMetersAggregateGross += otherGross;
                }
            }

            checked
            {
                currentAggregateGross = otherMetersAggregateGross + currentAllocations.TotalAmountMinor;
                projectedAggregateGross = otherMetersAggregateGross + projectedAllocations.TotalAmountMinor;
            }
        }
        catch (OverflowException)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_preview_invalid",
                "The projected charge is outside the range this preview can compute.",
                correlationId);
        }

        UsageCharge currentCharge;
        UsageCharge projectedCharge;
        UsageCharge additionalCharge;

        try
        {
            // A gross total that comfortably fits a long on its own can still overflow once tax
            // is added on top inside SubscriptionAmountCalculator.TaxBreakdownFor — checked there,
            // so it throws instead of silently wrapping. Caught here the same way the tier-walk
            // overflow above is, rather than left to bubble up as an unhandled 500.
            currentCharge = UsageChargeCalculator.Charge(currentAggregateGross, subscription.Price);
            projectedCharge = UsageChargeCalculator.Charge(projectedAggregateGross, subscription.Price);

            // The difference of two fully rated aggregate totals, never rated on its own — a tier
            // boundary the additional units cross, or a rounding step at the discount or tax
            // boundary, can price the same units differently depending on what came before them in
            // the period, or in another meter sharing the same invoice.
            additionalCharge = UsageChargeCalculator.Difference(projectedCharge, currentCharge);
        }
        catch (OverflowException)
        {
            return Failure(
                PaymentFailureKind.Validation,
                "subscription_usage_preview_invalid",
                "The projected charge is outside the range this preview can compute.",
                correlationId);
        }

        var response = new SubscriptionUsageOveragePreviewResponse
        {
            MeterKey = meter.MeterKey,
            UnitLabel = meter.UnitLabel,
            CurrencyCode = subscription.CurrencyCode,
            PeriodKey = period.Key,
            PeriodStartUtc = period.StartUtc,
            PeriodEndUtc = period.EndUtc,
            CalculatedAtUtc = now,
            IncludedQuantity = allowance,
            CurrentUsage = currentUsage,
            CurrentOverage = currentOverageUnits,
            AdditionalQuantity = request.AdditionalQuantity,
            ProjectedUsage = projectedUsage,
            ProjectedOverage = projectedOverageUnits,
            CurrentCharge = Describe(currentCharge),
            AdditionalCharge = Describe(additionalCharge),
            ProjectedPeriodCharge = Describe(projectedCharge),
            AdditionalTierBreakdown = additionalAllocations.Allocations
                .Select(Describe)
                .ToList(),
            Discount = new UsageOveragePreviewDiscountResponse
            {
                AutomaticBasisPoints = SubscriptionDiscountPresentation.RateOf(subscription.Price) ?? 0,
                // Stated explicitly: a promotional discount code never reaches metered overage,
                // today or in this preview — see UsageChargeCalculator's own remarks.
                PromotionalCodeApplied = false
            },
            Tax = new UsageOveragePreviewTaxResponse
            {
                RateBasisPoints = subscription.Price.TaxRateBasisPoints,
                Mode = (subscription.Price.TaxMode ?? TaxMode.Exclusive).ToString()
            },
            WritesUsage = false,
            ChargesPayment = false,
            FinalChargeDependsOnActualPeriodEndUsage = true
        };

        return SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Success(
            response, correlationId);
    }

    private static UsageChargeAmountsResponse Describe(UsageCharge charge) => new()
    {
        GrossMinor = charge.GrossMinor,
        AutomaticDiscountMinor = charge.AutomaticDiscountMinor,
        NetMinor = charge.NetMinor,
        TaxMinor = charge.TaxMinor,
        TotalMinor = charge.TotalMinor
    };

    private static UsageOverageTierAllocationResponse Describe(TierAllocation allocation) => new()
    {
        FromOverageQuantity = allocation.FromOverageQuantity,
        ToOverageQuantity = allocation.ToOverageQuantity,
        Units = allocation.Units,
        UnitAmountMinor = allocation.UnitAmountMinor,
        AmountMinor = allocation.AmountMinor
    };

    private static SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse> Failure(
        PaymentFailureKind kind,
        string errorCode,
        string errorMessage,
        string correlationId) =>
        SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>.Failure(
            kind, errorCode, errorMessage, correlationId);
}
