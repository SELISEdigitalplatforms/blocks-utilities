using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

/// <summary>
/// Copies a plan and price's terms into the immutable form a subscription carries.
/// </summary>
/// <remarks>
/// A copy, not a reference: editing the catalogue afterwards must not change what an existing
/// subscriber is entitled to or charged. Shared by subscribing and by changing plan — both need
/// the identical snapshot, taken the identical way.
/// </remarks>
internal static class SubscriptionSnapshotBuilder
{
    public static PlanSnapshot SnapshotOf(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new PlanSnapshot
        {
            PlanId = plan.ItemId,
            Code = plan.Code,
            DisplayName = plan.DisplayName,
            FeaturesJson = plan.FeaturesJson,
            UsageInterval = plan.UsageInterval,
            UsageIntervalCount = plan.UsageIntervalCount,
            RequirePaymentMethodUpfront = plan.RequirePaymentMethodUpfront,
            PlanVersion = plan.Version,
            Entitlements = plan.Entitlements
                .Select(entitlement => new PlanEntitlement
                {
                    Key = entitlement.Key,
                    LimitKind = entitlement.LimitKind,
                    Limit = entitlement.Limit,
                    MeterKey = entitlement.MeterKey,
                    UnitLabel = entitlement.UnitLabel
                })
                .ToList(),
            Meters = plan.Meters
                .Select(meter => new PlanMeter
                {
                    MeterKey = meter.MeterKey,
                    DisplayName = meter.DisplayName,
                    UnitLabel = meter.UnitLabel,
                    Aggregation = meter.Aggregation,
                    ResetPolicy = meter.ResetPolicy,
                    IncludedQuantity = meter.IncludedQuantity,
                    OverageAllowed = meter.OverageAllowed,
                    ThresholdPercents = [.. meter.ThresholdPercents],
                    RateTables = meter.RateTables
                        .Select(table => new MeterRateTable
                        {
                            CurrencyCode = table.CurrencyCode,
                            Tiers = table.Tiers
                                .Select(tier => new MeterTier
                                {
                                    UpToQuantity = tier.UpToQuantity,
                                    UnitAmountMinor = tier.UnitAmountMinor
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList(),
            QuantityItems = plan.QuantityItems
                .Select(item => new PlanQuantityItem
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    MinQuantity = item.MinQuantity,
                    MaxQuantity = item.MaxQuantity,
                    DefaultQuantity = item.DefaultQuantity,
                    // Copied, not referenced: a catalogue edit to the bands must never reprice a
                    // subscriber already holding them.
                    QuantityDiscountTiers = item.QuantityDiscountTiers
                        .Select(tier => new QuantityDiscountTier
                        {
                            MinimumQuantity = tier.MinimumQuantity,
                            MaximumQuantity = tier.MaximumQuantity,
                            DiscountBasisPoints = tier.DiscountBasisPoints
                        })
                        .ToList()
                })
                .ToList(),
            QuantityDiscountCombinationPolicy = plan.QuantityDiscountCombinationPolicy
        };
    }

    public static PriceSnapshot SnapshotOf(Price price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return new PriceSnapshot
        {
            PriceId = price.ItemId,
            CurrencyCode = price.CurrencyCode,
            UnitAmountMinor = price.UnitAmountMinor,
            Interval = price.Interval,
            IntervalCount = price.IntervalCount,
            // Snapshotted with the cadence it qualifies, for the same reason the cadence is:
            // re-authoring the catalogue must not move an existing subscriber's renewal date.
            BillingAlignment = price.BillingAlignment,
            // Copied so the opening stub can be priced without ever reading the monthly price
            // again — not at checkout, not at renewal, not by a recovery sweep.
            CalendarStubBasePriceId = price.CalendarStubBasePriceId,
            CalendarStubBaseUnitAmountMinor = price.CalendarStubBaseUnitAmountMinor,
            CalendarAnnualChargeTiming = price.CalendarAnnualChargeTiming,
            DisplayPriceNote = price.DisplayPriceNote,
            QuantityItemKey = price.QuantityItemKey,
            TaxRateBasisPoints = price.TaxRateBasisPoints,
            // Snapshotted with the rate, for the same reason the rate is: editing the catalogue's
            // tax must not reprice anybody already subscribed.
            TaxMode = price.TaxMode,
            // Likewise: an 8% yearly discount is part of what the subscriber bought, so
            // clearing it from the catalogue tomorrow leaves them holding it.
            AutomaticDiscountBasisPoints = price.AutomaticDiscountBasisPoints,
            QuantityDiscountCombination = price.QuantityDiscountCombination,
            PriceVersion = price.Version
        };
    }
}
