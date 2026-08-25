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
            FamilyCode = plan.FamilyCode,
            FamilyRank = plan.FamilyRank,
            UsageInterval = plan.UsageInterval.ToString(),
            UsageIntervalCount = plan.UsageIntervalCount,
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
                    DefaultQuantity = item.DefaultQuantity,
                    QuantityDiscountTiers = item.QuantityDiscountTiers
                        .Select(tier => new QuantityDiscountTierResponse
                        {
                            MinimumQuantity = tier.MinimumQuantity,
                            MaximumQuantity = tier.MaximumQuantity,
                            DiscountBasisPoints = tier.DiscountBasisPoints
                        })
                        .ToList()
                })
                .ToList(),
            QuantityDiscountCombinationPolicy =
                plan.QuantityDiscountCombinationPolicy.ToString(),
            Meters = plan.Meters
                .Select(meter => new PlanMeterResponse
                {
                    MeterKey = meter.MeterKey,
                    DisplayName = meter.DisplayName,
                    UnitLabel = meter.UnitLabel,
                    Aggregation = meter.Aggregation.ToString(),
                    ResetPolicy = meter.ResetPolicy.ToString(),
                    CarryForwardCap = meter.CarryForwardCap,
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
                    BillingAlignment = price.BillingAlignment.ToString(),
                    CalendarStubBasePriceId = price.CalendarStubBasePriceId,
                    CalendarStubBaseUnitAmountMinor = price.CalendarStubBaseUnitAmountMinor,
                    DisplayPriceNote = price.DisplayPriceNote,
                    QuantityItemKey = price.QuantityItemKey,
                    TaxRateBasisPoints = price.TaxRateBasisPoints,
                    // Reported as Exclusive for a legacy price carrying a rate and no mode, because
                    // that is how it is calculated. Absent for an untaxed price, where a mode would
                    // suggest a tax there is none of.
                    TaxMode = price.TaxRateBasisPoints > 0
                        ? (price.TaxMode ?? Enums.TaxMode.Exclusive).ToString()
                        : null,
                    AutomaticDiscountBasisPoints = price.AutomaticDiscountBasisPoints > 0
                        ? price.AutomaticDiscountBasisPoints
                        : null,
                    // Reported as BestDiscount when a discount was authored without one, because
                    // that is how it is calculated. Absent when there is no automatic discount, where
                    // a combination would describe a decision nobody has to make.
                    QuantityDiscountCombination = price.AutomaticDiscountBasisPoints > 0
                        ? (price.QuantityDiscountCombination
                            ?? Enums.AutomaticDiscountCombination.BestDiscount).ToString()
                        : null
                })
                .ToList()
        };
    }
}
