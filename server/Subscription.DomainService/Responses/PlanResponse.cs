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

    public long IncludedQuantity { get; init; }

    public bool OverageAllowed { get; init; }
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
