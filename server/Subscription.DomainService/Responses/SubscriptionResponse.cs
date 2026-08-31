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

    /// <summary>
    /// Kept for backward compatibility. Prefer <see cref="Cancellation"/>, which distinguishes
    /// when cancellation was requested from when access actually ends.
    /// </summary>
    public bool CancelAtPeriodEnd { get; init; }

    /// <summary>
    /// Kept for backward compatibility. This is the request time, not the moment access ends —
    /// see <see cref="Cancellation"/>'s <c>EffectiveAtUtc</c> for that.
    /// </summary>
    public DateTime? CanceledAtUtc { get; init; }

    /// <summary>
    /// The subscription's cancellation, if one has ever been requested. Null otherwise —
    /// including once the subscription itself has been superseded by a fresh signup, since a new
    /// subscription's own <c>ItemId</c> means its cancellation history starts over.
    /// </summary>
    public SubscriptionCancellationResponse? Cancellation { get; init; }

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
    /// The year this subscription has bought but not yet started, while it is inside its opening
    /// stub. Null at every other moment of its life.
    /// </summary>
    /// <remarks>
    /// Present so a client can show what the subscriber has actually committed to — a stub on
    /// screen with no sign of the year behind it reads as a much smaller purchase than it was.
    /// </remarks>
    public PendingAnnualPeriodResponse? PendingAnnualPeriod { get; init; }

    /// <summary>
    /// Where to send the customer to pay. Present only while the first charge is outstanding.
    /// </summary>
    public string? CheckoutUrl { get; init; }

    /// <summary>The non-financial checkout that must finish before access is granted.</summary>
    public PendingCheckoutResponse? PendingCheckout { get; init; }

    /// <summary>
    /// Whether a payment method is already on file, where that was actually checked -- null
    /// everywhere it was not.
    /// </summary>
    /// <remarks>
    /// Populated by <c>GET /subscriptions/current</c>, the one place a client has to decide
    /// whether to offer adding a card at all: a Trialing subscription whose trial never demanded
    /// one may already have a card if the subscriber added one voluntarily, or may still need
    /// this call's own CTA. Status alone cannot tell those apart, and null rather than false
    /// elsewhere is deliberate -- a bare <c>bool</c> defaulting to false would read as "no card"
    /// on a response that never checked, which is a wrong answer wearing a right one's shape.
    /// </remarks>
    public bool? HasPaymentMethod { get; init; }

    /// <summary>
    /// The overage terms this subscription actually bought, one entry per meter the plan
    /// defines. Empty for a legacy subscription whose snapshot predates metered usage -- never
    /// null, so a client can iterate without an extra guard.
    /// </summary>
    public List<MeterTermsResponse> Meters { get; init; } = [];

    public int Version { get; init; }
}

/// <summary>What a subscription's cancellation is doing, if one has been requested.</summary>
public sealed class SubscriptionCancellationResponse
{
    /// <summary>
    /// <c>"Scheduled"</c> while access continues through <see cref="EffectiveAtUtc"/>, or
    /// <c>"Effective"</c> once it has actually ended.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>When cancellation was asked for.</summary>
    public DateTime RequestedAtUtc { get; init; }

    /// <summary>
    /// When access ends. Equal to the subscription's <c>CurrentPeriodEndUtc</c> while
    /// <see cref="State"/> is <c>"Scheduled"</c> — the instant access actually stopped, once it
    /// is <c>"Effective"</c>.
    /// </summary>
    public DateTime EffectiveAtUtc { get; init; }

    /// <summary>
    /// Whether a scheduled cancellation may still be escalated to take effect now. False when it
    /// is locked to an already-paid annual term: escalating it would forfeit access the
    /// subscriber has paid for, so a request to do so is honoured as far as it safely can be —
    /// left scheduled, exactly as it already was. Meaningless once <see cref="State"/> is
    /// <c>"Effective"</c>.
    /// </summary>
    public bool CanCancelImmediately { get; init; }
}

public sealed class PendingCheckoutResponse
{
    /// <summary>Currently always PaymentMethodSetup; explicit for future checkout purposes.</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Pending, Failed, or Expired.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>A safe machine-readable failure reason, when the setup failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Only present while the hosted session remains usable.</summary>
    public string? CheckoutUrl { get; init; }
}

/// <summary>The year a calendar-aligned yearly subscription has bought but not yet started.</summary>
public sealed class PendingAnnualPeriodResponse
{
    public DateTime StartUtc { get; init; }

    public DateTime EndUtc { get; init; }

    public long AmountMinor { get; init; }

    public long NetAmountMinor { get; init; }

    public long TaxAmountMinor { get; init; }

    /// <summary>
    /// Whether the year was collected with the opening charge. False means it is still owed, and
    /// will be taken when the year begins.
    /// </summary>
    public bool IsPrepaid { get; init; }
}

public sealed class SubscriptionQuantityResponse
{
    public string ItemKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public long Quantity { get; init; }
}
