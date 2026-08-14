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
                    DefaultQuantity = item.DefaultQuantity
                })
                .ToList()
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
            QuantityItemKey = price.QuantityItemKey,
            PriceVersion = price.Version
        };
    }
}
