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

    public List<PlanQuantityItemResponse> QuantityItems { get; init; } = [];

    public List<PlanMeterResponse> Meters { get; init; } = [];

    public List<PlanEntitlementResponse> Entitlements { get; init; } = [];

    public List<PlanPriceResponse> Prices { get; init; } = [];
}

public sealed class PlanQuantityItemResponse
{
    public string ItemKey { get; init; } = string.Empty;

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

    public string? QuantityItemKey { get; init; }
}
