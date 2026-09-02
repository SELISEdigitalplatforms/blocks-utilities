using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

public sealed class PlanQuantityItemRequest
{
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>The product's own word for the unit: "seat", "user", "workspace".</summary>
    public string UnitLabel { get; set; } = string.Empty;

    public long MinQuantity { get; set; } = 1;

    public long? MaxQuantity { get; set; }

    public long DefaultQuantity { get; set; } = 1;

    /// <summary>Volume bands, ascending, gap-free from <see cref="MinQuantity"/>. Empty for one flat price.</summary>
    public List<QuantityDiscountTierRequest> QuantityDiscountTiers { get; set; } = [];
}

public sealed class QuantityDiscountTierRequest
{
    public long MinimumQuantity { get; set; }

    /// <summary>Null only on the final band.</summary>
    public long? MaximumQuantity { get; set; }

    /// <summary>Out of 10,000. 500 is 5%.</summary>
    public int DiscountBasisPoints { get; set; }
}

public sealed class PlanMeterRequest
{
    public string MeterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    public MeterResetPolicy ResetPolicy { get; set; } = MeterResetPolicy.Periodic;

    /// <summary>
    /// How many decimal places this meter's quantities may carry. Zero — whole units only — unless
    /// the author raises it, which is what keeps a meter counting screenings refusing half of one.
    /// </summary>
    public int QuantityScale { get; set; }

    public decimal IncludedQuantity { get; set; }

    /// <summary>Required when the reset policy carries forward; rejected otherwise.</summary>
    public decimal? CarryForwardCap { get; set; }

    public bool OverageAllowed { get; set; } = true;

    /// <summary>Percentages of the allowance that raise an event the first time they are crossed.</summary>
    public List<int> ThresholdPercents { get; set; } = [];

    public List<MeterRateTableRequest> RateTables { get; set; } = [];
}

public sealed class MeterRateTableRequest
{
    public string CurrencyCode { get; set; } = string.Empty;

    public List<MeterTierRequest> Tiers { get; set; } = [];
}

public sealed class MeterTierRequest
{
    /// <summary>Upper bound of the band. Null is the final, unbounded one.</summary>
    public decimal? UpToQuantity { get; set; }

    public long UnitAmountMinor { get; set; }
}

public sealed class PlanEntitlementRequest
{
    public string Key { get; set; } = string.Empty;

    public EntitlementLimitKind LimitKind { get; set; } = EntitlementLimitKind.Boolean;

    public decimal? Limit { get; set; }

    public string? MeterKey { get; set; }

    public string? UnitLabel { get; set; }
}

public sealed class TrialGrantRequest
{
    public string MeterKey { get; set; } = string.Empty;

    public decimal IncludedQuantity { get; set; }
}
