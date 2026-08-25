namespace Subscription.DomainService.Responses;

/// <summary>
/// A subscription as its owner sees it.
/// </summary>
public sealed class SubscriptionResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public long UnitAmountMinor { get; init; }

    public string Interval { get; init; } = string.Empty;

    public int IntervalCount { get; init; }

    public string? DisplayPriceNote { get; init; }

    public List<SubscriptionQuantityResponse> Quantities { get; init; } = [];

    public DateTime CurrentPeriodStartUtc { get; init; }

    public DateTime CurrentPeriodEndUtc { get; init; }

    /// <summary>When the next payment is expected. Null once cancellation is pending.</summary>
    public DateTime? NextPaymentAtUtc { get; init; }

    public DateTime? TrialEndsAtUtc { get; init; }

    public bool CancelAtPeriodEnd { get; init; }

    public DateTime? CanceledAtUtc { get; init; }

    /// <summary>
    /// The reduction waiting for this period to end, if one is scheduled.
    /// </summary>
    /// <remarks>
    /// On the ordinary subscription read, not only on the response to the request that scheduled
    /// it. Without it a page reload shows the quantity still in force with nothing to say a smaller
    /// one is already booked, and the client has no way to know there is anything to cancel.
    /// </remarks>
    public PendingQuantityChangeResponse? PendingQuantityChange { get; init; }

    /// <summary>The volume band the quantity in force selects, if the plan defines any.</summary>
    public QuantityDiscountTierResponse? CurrentTier { get; init; }

    /// <summary>
    /// What the next renewal costs at the quantity, band and discount in force — the figure a
    /// client would otherwise have to reconstruct from the unit amount and guess at.
    /// </summary>
    public long RecurringAmountMinor { get; init; }

    /// <summary>The tax inside <see cref="RecurringAmountMinor"/>.</summary>
    public long TaxAmountMinor { get; init; }

    /// <summary>What that tax is charged on: the renewal after discounts, before tax.</summary>
    public long NetAmountMinor { get; init; }

    /// <summary>Basis points on the price this subscription was sold on. Null when untaxed.</summary>
    public int? TaxRateBasisPoints { get; init; }

    /// <summary>
    /// "Exclusive" or "Inclusive", so a client can say whether the amount above is what the
    /// subscriber pays or what the tax is added to. Null when the price carries no tax.
    /// </summary>
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
    /// "Anniversary" or "CalendarMonth" — where this subscription's renewals land.
    /// </summary>
    /// <remarks>
    /// From the subscription's own snapshot, not the catalogue. Re-authoring the price a
    /// subscriber was sold on does not move their renewal date, so the catalogue is the wrong
    /// place to read this from.
    /// </remarks>
    public string BillingAlignment { get; init; } = string.Empty;

    /// <summary>
    /// What the first charge was fixed at. Null when nothing was ever charged for a first period
    /// — a card-free trial, or a subscription created before this was recorded.
    /// </summary>
    /// <remarks>
    /// Populated while a checkout is pending, which is when a client actually needs it, and kept
    /// afterwards so support can still answer "why this number" once the period has closed.
    /// <see cref="RecurringAmountMinor"/> is unaffected throughout: it is what the next full
    /// month costs, which is a different question from what the opening stub cost.
    /// </remarks>
    public long? InitialChargeAmountMinor { get; init; }

    /// <summary>Whether that first charge covered part of a month rather than all of one.</summary>
    public bool InitialChargeProrated { get; init; }

    /// <summary>Calendar dates the first period covered — the 7 of "7/31".</summary>
    public int? ProrationDays { get; init; }

    /// <summary>Dates in the month it was a fraction of — the 31 of "7/31".</summary>
    public int? ProrationTotalDays { get; init; }

    /// <summary>
    /// The monthly amount a calendar-aligned yearly subscription's opening stub was charged from.
    /// </summary>
    /// <remarks>
    /// From the subscription's own snapshot, so it answers "what was this stub a fraction of"
    /// years later, whatever has happened to the catalogue since. Null unless the subscription is
    /// on a calendar-aligned yearly price.
    /// </remarks>
    public long? CalendarStubBaseUnitAmountMinor { get; init; }

    /// <summary>
    /// Where to send the customer to pay. Present only while the first charge is outstanding.
    /// </summary>
    public string? CheckoutUrl { get; init; }

    public int Version { get; init; }
}

public sealed class SubscriptionQuantityResponse
{
    public string ItemKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public long Quantity { get; init; }
}
