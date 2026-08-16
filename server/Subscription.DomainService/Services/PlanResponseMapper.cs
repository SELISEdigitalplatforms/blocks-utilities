using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Turns stored plans into responses.
/// </summary>
/// <remarks>
/// Its own type rather than a method on the service, so the service orchestrates and this
/// decides shape. Enum values cross the wire as their names: a client that has to know
/// <c>2</c> means monthly is coupled to our storage format.
/// </remarks>
public sealed class PlanResponseMapper : IPlanResponseMapper
{
    public PlanResponse ToResponse(
        Plan plan,
        IReadOnlyList<Price> prices,
        bool hasSubscribers = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prices);

        return new PlanResponse
        {
            PlanId = plan.ItemId,
            Code = plan.Code,
            DisplayName = plan.DisplayName,
            Description = plan.Description,
            OrganizationId = plan.OrganizationId,
            FeaturesJson = plan.FeaturesJson,
            TrialDays = plan.TrialDays,
            TrialRequiresPaymentMethod = plan.TrialRequiresPaymentMethod,
            Version = plan.Version,
            HasSubscribers = hasSubscribers,
            QuantityItems = plan.QuantityItems
                .Select(item => new PlanQuantityItemResponse
                {
                    ItemKey = item.ItemKey,
                    UnitLabel = item.UnitLabel,
                    MinQuantity = item.MinQuantity,
                    MaxQuantity = item.MaxQuantity,
                    DefaultQuantity = item.DefaultQuantity
                })
                .ToList(),
            Meters = plan.Meters
                .Select(meter => new PlanMeterResponse
                {
                    MeterKey = meter.MeterKey,
                    DisplayName = meter.DisplayName,
                    UnitLabel = meter.UnitLabel,
                    Aggregation = meter.Aggregation.ToString(),
                    IncludedQuantity = meter.IncludedQuantity,
                    OverageAllowed = meter.OverageAllowed,
                    ThresholdPercents = [.. meter.ThresholdPercents],
                    RateTables = meter.RateTables
                        .Select(table => new PlanMeterRateTableResponse
                        {
                            CurrencyCode = table.CurrencyCode,
                            Tiers = table.Tiers
                                .Select(tier => new PlanMeterTierResponse
                                {
                                    UpToQuantity = tier.UpToQuantity,
                                    UnitAmountMinor = tier.UnitAmountMinor
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList(),
            Entitlements = plan.Entitlements
                .Select(entitlement => new PlanEntitlementResponse
                {
                    Key = entitlement.Key,
                    LimitKind = entitlement.LimitKind.ToString(),
                    Limit = entitlement.Limit,
                    MeterKey = entitlement.MeterKey,
                    UnitLabel = entitlement.UnitLabel
                })
                .ToList(),
            TrialGrants = plan.TrialGrants
                .Select(grant => new PlanTrialGrantResponse
                {
                    MeterKey = grant.MeterKey,
                    IncludedQuantity = grant.IncludedQuantity
                })
                .ToList(),
            Prices = prices
                .Select(price => new PlanPriceResponse
                {
                    PriceId = price.ItemId,
                    CurrencyCode = price.CurrencyCode,
                    UnitAmountMinor = price.UnitAmountMinor,
                    Interval = price.Interval.ToString(),
                    IntervalCount = price.IntervalCount,
                    QuantityItemKey = price.QuantityItemKey
                })
                .ToList()
        };
    }
}
