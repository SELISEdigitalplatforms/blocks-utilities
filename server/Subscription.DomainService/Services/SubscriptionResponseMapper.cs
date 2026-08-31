using Payment.DomainService.Services;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionResponseMapper : ISubscriptionResponseMapper
{
    private readonly TimeProvider _time;
    private readonly ICurrencyMinorUnitResolver _currency;

    /// <summary>
    /// <paramref name="currency"/> defaults to a resolver that reports every currency
    /// unresolvable, matching how DI actually wires this in production -- the payment module
    /// registers the real one as a singleton, so an explicit default here only ever applies to a
    /// caller (chiefly a test) that has no reason to price a meter's overage in the first place.
    /// </summary>
    public SubscriptionResponseMapper(
        TimeProvider? time = null,
        ICurrencyMinorUnitResolver? currency = null)
    {
        _time = time ?? TimeProvider.System;
        _currency = currency ?? UnresolvedCurrencyMinorUnitResolver.Instance;
    }

    public SubscriptionResponse ToResponse(
        SubscriptionDetail subscription,
        string? checkoutUrl = null,
        PendingCheckoutResponse? pendingCheckout = null,
        bool? hasPaymentMethod = null)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        var band = QuantityDiscountCalculator.ResolveFrom(
            subscription.Plan,
            subscription.Price,
            subscription.QuantityItems);

        // Priced through the same path the renewal itself uses, so what is shown cannot drift from
        // what is taken.
        var recurring = SubscriptionAmountCalculator.PeriodAmountMinor(
            subscription,
            _time.GetUtcNow().UtcDateTime);

        return new SubscriptionResponse
        {
            SubscriptionId = subscription.ItemId,
            Status = subscription.Status.ToString(),
            PlanCode = subscription.Plan.Code,
            PlanName = subscription.Plan.DisplayName,
            CurrencyCode = subscription.CurrencyCode,
            UnitAmountMinor = subscription.Price.UnitAmountMinor,
            Interval = subscription.Price.Interval.ToString(),
            IntervalCount = subscription.Price.IntervalCount,
            UsageInterval = subscription.Plan.UsageInterval.ToString(),
            UsageIntervalCount = subscription.Plan.UsageIntervalCount,
            DisplayPriceNote = subscription.Price.DisplayPriceNote,
            Quantities = subscription.QuantityItems
                .Select(item => new SubscriptionQuantityResponse
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    Quantity = item.Quantity
                })
                .ToList(),
            CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc,
            CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
            // A prepaid pending year still needs processing at its start boundary, but no money is
            // taken there. Report the next actual payment at the end of the bought year while the
            // repository keeps NextFeeBillingAtUtc for the worker transition.
            NextPaymentAtUtc = subscription.PendingAnnualPeriod is { IsPrepaid: true } prepaidAnnual
                ? prepaidAnnual.EndUtc
                : subscription.NextFeeBillingAtUtc,
            TrialEndsAtUtc = subscription.Trial?.EndsAtUtc,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CanceledAtUtc = subscription.CanceledAtUtc,
            Cancellation = subscription.CanceledAtUtc is { } requestedAtUtc
                ? new SubscriptionCancellationResponse
                {
                    State = subscription.Status == SubscriptionStatus.Canceled
                        ? "Effective"
                        : "Scheduled",
                    RequestedAtUtc = requestedAtUtc,
                    EffectiveAtUtc = subscription.Status == SubscriptionStatus.Canceled
                        ? subscription.EndedAtUtc ?? requestedAtUtc
                        : subscription.CurrentPeriodEndUtc,
                    CanCancelImmediately = subscription.CanCancelImmediately
                }
                : null,
            PendingQuantityChange = QuantityResponseMapper.Pending(subscription.PendingQuantityChange),
            CurrentTier = QuantityResponseMapper.Tier(band.Tier),
            RecurringAmountMinor = recurring.AmountMinor,
            TaxAmountMinor = recurring.TaxAmountMinor,
            NetAmountMinor = recurring.NetAmountMinor,
            TaxRateBasisPoints = subscription.Price.TaxRateBasisPoints,
            TaxMode = SubscriptionTaxPresentation.Describe(subscription.Price),
            AutomaticDiscountBasisPoints =
                SubscriptionDiscountPresentation.RateOf(subscription.Price),
            QuantityDiscountCombination =
                SubscriptionDiscountPresentation.Describe(subscription.Price),
            GrossAmountMinor = recurring.GrossAmountMinor,
            BuiltInDiscountMinor = recurring.BuiltInDiscountMinor,
            PromotionalDiscountMinor = recurring.PromotionalDiscountMinor,
            DiscountedAmountMinor = recurring.GrossAmountMinor
                - recurring.BuiltInDiscountMinor
                - recurring.PromotionalDiscountMinor,
            BillingAlignment = subscription.Price.BillingAlignment.ToString(),
            InitialChargeAmountMinor = subscription.InitialChargeAmountMinor,
            InitialChargeProrated = subscription.InitialChargeProrated,
            ProrationDays = subscription.ProrationDays,
            ProrationTotalDays = subscription.ProrationTotalDays,
            CalendarStubBaseUnitAmountMinor = subscription.Price.CalendarStubBaseUnitAmountMinor,
            PendingAnnualPeriod = subscription.PendingAnnualPeriod is { } pending
                ? new PendingAnnualPeriodResponse
                {
                    StartUtc = pending.StartUtc,
                    EndUtc = pending.EndUtc,
                    AmountMinor = pending.AmountMinor,
                    NetAmountMinor = pending.NetAmountMinor,
                    TaxAmountMinor = pending.TaxAmountMinor,
                    IsPrepaid = pending.IsPrepaid
                }
                : null,
            CheckoutUrl = checkoutUrl,
            PendingCheckout = pendingCheckout,
            HasPaymentMethod = hasPaymentMethod,
            Meters = subscription.Plan.Meters
                .Select(meter => ToMeterTerms(meter, subscription.CurrencyCode))
                .ToList(),
            Version = subscription.Version
        };
    }

    private MeterTermsResponse ToMeterTerms(PlanMeter meter, string currencyCode) => new()
    {
        MeterKey = meter.MeterKey,
        DisplayName = meter.DisplayName,
        UnitLabel = meter.UnitLabel,
        IncludedQuantity = meter.IncludedQuantity,
        ResetPolicy = meter.ResetPolicy.ToString(),
        CarryForwardCap = meter.CarryForwardCap,
        OverageAllowed = meter.OverageAllowed,
        OveragePricing = meter.OverageAllowed
            ? ResolveOveragePricing(meter, currencyCode)
            : null
    };

    private OveragePricingResponse? ResolveOveragePricing(PlanMeter meter, string currencyCode)
    {
        var table = meter.RateTables.Find(candidate =>
            string.Equals(candidate.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase));

        // Overage allowed with no rate table for this subscription's currency, and overage
        // allowed with a rate table this subscription's currency does not match, both read the
        // same way to a client: allowed, but nothing here prices it.
        if (table is null)
        {
            return null;
        }

        var tiers = new List<OverageTierResponse>(table.Tiers.Count);

        foreach (var tier in table.Tiers)
        {
            if (!MinorUnitMajorAmountFormatter.TryFormat(
                _currency, tier.UnitAmountMinor, currencyCode, out var amount))
            {
                // A rate table naming a currency the payment module can no longer resolve is a
                // configuration gap, not something to fabricate a conversion for. Report the
                // whole tier list unavailable rather than a partially-priced one -- a client
                // cannot use "the first two tiers converted, the third did not" for anything.
                return null;
            }

            tiers.Add(new OverageTierResponse
            {
                UpToQuantity = tier.UpToQuantity,
                UnitAmount = amount
            });
        }

        return new OveragePricingResponse
        {
            CurrencyCode = currencyCode,
            Tiers = tiers
        };
    }

    /// <summary>
    /// Resolves nothing, honestly, so a mapper built without a real currency resolver never
    /// fabricates a conversion -- it simply reports every meter's overage as unpriced, which is
    /// the correct answer for a caller that never wired one in.
    /// </summary>
    private sealed class UnresolvedCurrencyMinorUnitResolver : ICurrencyMinorUnitResolver
    {
        public static readonly UnresolvedCurrencyMinorUnitResolver Instance = new();

        public bool TryConvert(decimal amount, string currencyCode, out long minorUnits)
        {
            minorUnits = 0;
            return false;
        }

        public bool TryConvertBack(long minorUnits, string currencyCode, out decimal amount)
        {
            amount = 0;
            return false;
        }
    }
}
