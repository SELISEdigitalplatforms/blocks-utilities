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

    /// <summary>
    /// What one unit costs at the new quantity, after every reduction and <em>before</em> tax.
    /// Null when the price is a flat fee.
    /// </summary>
    /// <remarks>
    /// Stated here because it cannot be derived from the unit amount and the band alone: the
    /// promotion on the subscription, the plan's combination policy and this module's rounding all
    /// move it, and a client that multiplies a percentage against the list price shows a figure
    /// that disagrees with what it is about to charge.
    /// <para>
    /// Before tax, deliberately. Tax is a proportion of the whole charge rather than a property of
    /// a unit, and folding it in produced a "per unit" figure <em>above</em> the list price on a
    /// screen that also said "5% off" — arithmetic nobody could follow.
    /// <see cref="TaxAmountMinor"/> carries it instead.
    /// </para>
    /// <para>
    /// Null rather than the plan's fee where the price has no quantity item. A flat fee is not sold
    /// by the unit, and a plan that merely tracks a free quantity item would otherwise report its
    /// whole fee as the price of each one.
    /// </para>
    /// </remarks>
    public long? EffectiveUnitAmountMinor { get; init; }

    /// <summary>The tax inside <see cref="NextRenewalAmountMinor"/>, which is tax-inclusive.</summary>
    public long TaxAmountMinor { get; init; }

    /// <summary>What is taxed: the renewal after discounts, before tax and before credit.</summary>
    /// <remarks>
    /// Stated rather than left to subtraction. With an inclusive price the total is the configured
    /// amount and the net is <em>below</em> it, so a client deriving one from the other has to know
    /// which mode it is in — and <see cref="TaxMode"/> is here for presentation, not arithmetic.
    /// </remarks>
    public long NetAmountMinor { get; init; }

    /// <summary>Banked credit spent against this renewal, already deducted above.</summary>
    public long CreditConsumedMinor { get; init; }

    /// <summary>Basis points on the subscription's own snapshotted price. Null when untaxed.</summary>
    public int? TaxRateBasisPoints { get; init; }

    /// <summary>"Exclusive" or "Inclusive", so a client can say which the amount already contains.</summary>
    public string? TaxMode { get; init; }

    /// <summary>Basis points taken off automatically by the price itself. Null when it has none.</summary>
    public int? AutomaticDiscountBasisPoints { get; init; }

    /// <summary>
    /// "BestDiscount" or "Additive" — how the automatic discount met the volume band. Null when
    /// there is no automatic discount to combine.
    /// </summary>
    public string? QuantityDiscountCombination { get; init; }

    /// <summary>The charge before any reduction, so the ones below have something to be off of.</summary>
    public long GrossAmountMinor { get; init; }

    /// <summary>
    /// What the automatic discount and the volume band took off between them, already combined the
    /// way the price says to. Zero when neither applied.
    /// </summary>
    public long BuiltInDiscountMinor { get; init; }

    /// <summary>
    /// What the subscriber's promotional code took off, after the built-in reduction was settled.
    /// Zero when there is no code, or when the plan's policy left it unused.
    /// </summary>
    public long PromotionalDiscountMinor { get; init; }

    /// <summary>
    /// What is left to tax: gross less both reductions above. The same figure
    /// <see cref="NetAmountMinor"/> reports for a tax-exclusive price, stated separately because for
    /// an inclusive one the net is below it by the tax inside.
    /// </summary>
    public long DiscountedAmountMinor { get; init; }


    /// <summary>
    /// Whether a promotional discount is part of the figures above, so a client can say why the
    /// unit price is below the band's own arithmetic rather than leaving it unexplained.
    /// </summary>
    public bool PromotionApplied { get; init; }

    /// <summary>Conditions that would make confirmation fail. Populated on previews only.</summary>
    public List<SubscriptionPreviewBlockerResponse> Blockers { get; init; } = [];

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
