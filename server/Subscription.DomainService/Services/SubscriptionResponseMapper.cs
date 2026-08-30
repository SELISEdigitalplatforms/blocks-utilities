using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionResponseMapper : ISubscriptionResponseMapper
{
    private readonly TimeProvider _time;

    public SubscriptionResponseMapper(TimeProvider? time = null) =>
        _time = time ?? TimeProvider.System;

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
            NextPaymentAtUtc = subscription.NextFeeBillingAtUtc,
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
            Version = subscription.Version
        };
    }
}
