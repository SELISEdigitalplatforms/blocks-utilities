namespace Subscription.DomainService.Responses;

/// <summary>
/// What a quantity change costs and when it takes effect — the same shape whether it was
/// previewed or applied.
/// </summary>
/// <remarks>
/// One shape for both so a client can render the confirmation and the outcome from the same code,
/// and so a preview cannot describe the change in terms the apply does not use.
/// </remarks>
public sealed class QuantityChangeResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>The version after the change. Unchanged on a preview.</summary>
    public int Version { get; init; }

    /// <summary>Whether this was calculated only, or committed.</summary>
    public bool Preview { get; init; }

    /// <summary>
    /// <c>Immediate</c> for an increase, <c>NextPeriod</c> for a decrease.
    /// </summary>
    public string Timing { get; init; } = string.Empty;

    /// <summary>When the new quantity starts applying.</summary>
    public DateTime EffectiveAtUtc { get; init; }

    /// <summary>The quantities that will be in force from <see cref="EffectiveAtUtc"/>.</summary>
    public List<QuantityChangeItemResponse> Quantities { get; init; } = [];

    /// <summary>The band the current quantity selects, before the change.</summary>
    public QuantityDiscountTierResponse? CurrentTier { get; init; }

    /// <summary>The band the requested quantity selects.</summary>
    public QuantityDiscountTierResponse? TargetTier { get; init; }

    /// <summary>
    /// What is owed now for the rest of the current period. Zero for a decrease, which is never
    /// refunded and never charged.
    /// </summary>
    public long ProratedChargeMinor { get; init; }

    /// <summary>What the next renewal will charge at the new quantity and band.</summary>
    public long NextRenewalAmountMinor { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>The payment taken for an applied increase. Null on a preview or a decrease.</summary>
    public string? ChargePaymentDetailId { get; init; }

    /// <summary>The decrease waiting for the period to end, if one is now scheduled.</summary>
    public PendingQuantityChangeResponse? PendingQuantityChange { get; init; }
}

public sealed class QuantityChangeItemResponse
{
    public string ItemKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public long Quantity { get; init; }
}

public sealed class PendingQuantityChangeResponse
{
    public List<QuantityChangeItemResponse> Quantities { get; init; } = [];

    public DateTime RequestedAtUtc { get; init; }

    public DateTime EffectiveAtUtc { get; init; }
}
