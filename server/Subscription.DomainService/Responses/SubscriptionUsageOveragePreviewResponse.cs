namespace Subscription.DomainService.Responses;

/// <summary>
/// What a hypothetical slice of additional metered usage would cost, rated with the same terms
/// and the same order of operations period-end usage rating would eventually charge — but
/// nothing here is recorded, and nothing here is charged.
/// </summary>
public sealed class SubscriptionUsageOveragePreviewResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    public DateTime PeriodStartUtc { get; init; }

    public DateTime PeriodEndUtc { get; init; }

    /// <summary>
    /// When this preview was computed. Usage recorded after this instant can change the eventual
    /// invoice — this is an estimate, not a hold.
    /// </summary>
    public DateTime CalculatedAtUtc { get; init; }

    /// <summary>
    /// The effective allowance for this window, including a trial grant or carried-forward
    /// allowance — see <c>IMeterAllowanceResolver</c>.
    /// </summary>
    public decimal IncludedQuantity { get; init; }

    public decimal CurrentUsage { get; init; }

    public decimal CurrentOverage { get; init; }

    public decimal AdditionalQuantity { get; init; }

    public decimal ProjectedUsage { get; init; }

    public decimal ProjectedOverage { get; init; }

    /// <summary>What the period would already owe, rated from usage recorded so far.</summary>
    public UsageChargeAmountsResponse CurrentCharge { get; init; } = new();

    /// <summary>
    /// What the additional usage alone would add — the difference between the projected and
    /// current charges, never rated on its own. See the endpoint documentation for why.
    /// </summary>
    public UsageChargeAmountsResponse AdditionalCharge { get; init; } = new();

    /// <summary>What the whole period would owe if the additional usage happened right now.</summary>
    public UsageChargeAmountsResponse ProjectedPeriodCharge { get; init; } = new();

    /// <summary>
    /// Which graduated tier bands the additional usage would fall into, and what each contributed.
    /// Informational only — the amounts here are not independently taxed or discounted; the
    /// authoritative additional charge is <see cref="AdditionalCharge"/>.
    /// </summary>
    public IReadOnlyList<UsageOverageTierAllocationResponse> AdditionalTierBreakdown { get; init; } = [];

    public UsageOveragePreviewDiscountResponse Discount { get; init; } = new();

    public UsageOveragePreviewTaxResponse Tax { get; init; } = new();

    /// <summary>Always false. Nothing this call returns is recorded against the usage ledger.</summary>
    public bool WritesUsage { get; init; }

    /// <summary>Always false. This call never charges a payment method.</summary>
    public bool ChargesPayment { get; init; }

    /// <summary>
    /// Always true. Usage recorded between now and period end changes the actual invoice; this
    /// response is an estimate of what period-end rating would charge if nothing else changed.
    /// </summary>
    public bool FinalChargeDependsOnActualPeriodEndUsage { get; init; } = true;
}

/// <summary>One charge, split the way an invoice line would be.</summary>
public sealed class UsageChargeAmountsResponse
{
    public long GrossMinor { get; init; }

    public long AutomaticDiscountMinor { get; init; }

    public long NetMinor { get; init; }

    public long TaxMinor { get; init; }

    public long TotalMinor { get; init; }
}

/// <summary>One graduated tier band's slice of the additional usage.</summary>
public sealed class UsageOverageTierAllocationResponse
{
    /// <summary>
    /// The first overage unit this band covers, counted from the first overage unit of the whole
    /// period — not from wherever the additional usage itself begins.
    /// </summary>
    public decimal FromOverageQuantity { get; init; }

    public decimal ToOverageQuantity { get; init; }

    public decimal Units { get; init; }

    public long UnitAmountMinor { get; init; }

    public decimal AmountMinor { get; init; }
}

/// <summary>
/// The reduction that reaches metered overage, and the one that explicitly does not.
/// </summary>
public sealed class UsageOveragePreviewDiscountResponse
{
    public int AutomaticBasisPoints { get; init; }

    /// <summary>
    /// Always false. A promotional discount code never reduces metered overage — stated
    /// explicitly here rather than left for a client to wonder whether one was simply missed.
    /// </summary>
    public bool PromotionalCodeApplied { get; init; }
}

public sealed class UsageOveragePreviewTaxResponse
{
    public int? RateBasisPoints { get; init; }

    public string Mode { get; init; } = string.Empty;
}
