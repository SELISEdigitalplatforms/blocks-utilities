namespace Subscription.DomainService.Responses;

/// <summary>
/// A plan as a caller sees it.
/// </summary>
/// <remarks>
/// Deliberately not the entity. The stored document carries a version, timestamps and the
/// tenant it belongs to; a response that returned them would make internal bookkeeping part of
/// the public contract.
/// </remarks>
public sealed class PlanResponse
{
    public string PlanId { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? FamilyCode { get; init; }

    public int? FamilyRank { get; init; }

    public string UsageInterval { get; init; } = string.Empty;

    public int UsageIntervalCount { get; init; }

    /// <summary>
    /// The organization this plan is scoped to, or null when the tenant sells it to everyone.
    /// Returned so a caller that can see plans from more than one organization — the console —
    /// can tell them apart; a plan only reaches a caller who was already entitled to see it.
    /// </summary>
    public string? OrganizationId { get; init; }

    /// <summary>The plan's own feature bag, exactly as it was authored.</summary>
    public string? FeaturesJson { get; init; }

    public int? TrialDays { get; init; }

    public bool TrialRequiresPaymentMethod { get; init; }

    public int Version { get; init; }

    /// <summary>
    /// Whether anything has ever subscribed to this plan. True closes editing: subscribing copies
    /// the plan's terms onto the subscription, so a plan that was sold has a history that editing
    /// the catalogue entry would contradict. Returned so a caller can say why before offering the
    /// edit, rather than after a form has been filled in.
    /// </summary>
    public bool HasSubscribers { get; init; }

    public List<PlanQuantityItemResponse> QuantityItems { get; init; } = [];

    /// <summary>How a volume band combines with a promotional code, as its name.</summary>
    public string QuantityDiscountCombinationPolicy { get; init; } = string.Empty;

    public List<PlanMeterResponse> Meters { get; init; } = [];

    public List<PlanEntitlementResponse> Entitlements { get; init; } = [];

    public List<PlanPriceResponse> Prices { get; init; } = [];

    /// <summary>
    /// How much of each meter a trial includes. Returned because an edit rewrites the whole plan:
    /// what a caller cannot read back, it cannot preserve, and the grants would be dropped by
    /// anyone editing anything else.
    /// </summary>
    public List<PlanTrialGrantResponse> TrialGrants { get; init; } = [];
}

public sealed class PlanTrialGrantResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public long IncludedQuantity { get; init; }
}

public sealed class QuantityDiscountTierResponse
{
    public long MinimumQuantity { get; init; }

    /// <summary>Null on the final, open-ended band.</summary>
    public long? MaximumQuantity { get; init; }

    /// <summary>Out of 10,000. 500 is 5%.</summary>
    public int DiscountBasisPoints { get; init; }
}

public sealed class PlanQuantityItemResponse
{
    public string ItemKey { get; init; } = string.Empty;

    /// <summary>
    /// Volume bands, ascending. Returned because an edit rewrites the whole plan: what a caller
    /// cannot read back, it cannot preserve.
    /// </summary>
    public List<QuantityDiscountTierResponse> QuantityDiscountTiers { get; init; } = [];

    public string UnitLabel { get; init; } = string.Empty;

    public long MinQuantity { get; init; }

    public long? MaxQuantity { get; init; }

    public long DefaultQuantity { get; init; }
}

public sealed class PlanMeterResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    /// <summary>How recordings combine, as its name — see <c>MeterAggregation</c>.</summary>
    public string Aggregation { get; init; } = string.Empty;

    public string ResetPolicy { get; init; } = string.Empty;

    /// <summary>The ceiling on what one window may carry in. Null unless the policy carries forward.</summary>
    public long? CarryForwardCap { get; init; }

    public long IncludedQuantity { get; init; }

    public bool OverageAllowed { get; init; }

    /// <summary>
    /// Percentages of the allowance that raise an event the first time they are crossed.
    /// Returned so whoever authored the plan can see what it will actually notify on.
    /// </summary>
    public List<int> ThresholdPercents { get; init; } = [];

    /// <summary>
    /// What usage past the allowance costs, per currency. Empty means overage cannot be priced
    /// — it is recorded and permitted but charged nothing, so the portal has to be able to show
    /// that a meter allowing overage has no table behind it.
    /// </summary>
    public List<PlanMeterRateTableResponse> RateTables { get; init; } = [];
}

public sealed class PlanMeterRateTableResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    public List<PlanMeterTierResponse> Tiers { get; init; } = [];
}

public sealed class PlanMeterTierResponse
{
    /// <summary>Upper bound of the band. Null is the final, unbounded one.</summary>
    public long? UpToQuantity { get; init; }

    public long UnitAmountMinor { get; init; }
}

public sealed class PlanEntitlementResponse
{
    public string Key { get; init; } = string.Empty;

    public string LimitKind { get; init; } = string.Empty;

    public long? Limit { get; init; }

    public string? MeterKey { get; init; }

    public string? UnitLabel { get; init; }
}

public sealed class PlanPriceResponse
{
    public string PriceId { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public long UnitAmountMinor { get; init; }

    public string Interval { get; init; } = string.Empty;

    public int IntervalCount { get; init; }

    /// <summary>
    /// "Anniversary" or "CalendarMonth", as its name. Always present — a client showing a price
    /// cannot say when it renews without it, and every price has an answer.
    /// </summary>
    public string BillingAlignment { get; init; } = string.Empty;

    public string? DisplayPriceNote { get; init; }

    public string? QuantityItemKey { get; init; }

    /// <summary>Basis points — 770 is 7.7%. Absent when the price is untaxed.</summary>
    public int? TaxRateBasisPoints { get; init; }

    /// <summary>
    /// "Exclusive" or "Inclusive". Reported for any price carrying a rate, including those authored
    /// before modes existed — a client cannot present a price correctly without knowing which of the
    /// two the amount is.
    /// </summary>
    public string? TaxMode { get; init; }

    /// <summary>Basis points off without a code — 800 is 8%. Absent when the price has none.</summary>
    public int? AutomaticDiscountBasisPoints { get; init; }

    /// <summary>
    /// "BestDiscount" or "Additive". Reported for any price carrying an automatic discount, since the
    /// two answers differ by real money once a volume band is also in play.
    /// </summary>
    public string? QuantityDiscountCombination { get; init; }
}
