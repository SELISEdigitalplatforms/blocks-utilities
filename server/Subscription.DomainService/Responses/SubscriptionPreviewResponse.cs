namespace Subscription.DomainService.Responses;

/// <summary>
/// What a subscription would cost if bought right now, without buying it.
/// </summary>
/// <remarks>
/// Every figure here is read from a subscription built exactly as <c>POST /subscriptions</c>
/// builds one, stopped one step short of being saved — the same plan and price resolution, the
/// same discount validation, the same schedule and proration arithmetic. There is one pricing
/// path, not two, so this cannot state an amount the confirm then charges something else for.
/// <para>
/// <see cref="TotalDueNowMinor"/> is that subscription's own frozen initial charge — the exact
/// expression <c>SubscriptionCheckoutService</c> reads when it actually takes payment.
/// </para>
/// </remarks>
public sealed class SubscriptionPreviewResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    /// <summary>The undiscounted amount for the period covered, before tax.</summary>
    public long SubtotalMinor { get; init; }

    /// <summary>Every reduction combined — the figure the specified contract asks for.</summary>
    public long DiscountMinor { get; init; }

    /// <summary>What the price's own automatic discount and volume band took off.</summary>
    public long BuiltInDiscountMinor { get; init; }

    /// <summary>What a promotional code took off, if one was supplied and applied.</summary>
    public long PromotionalDiscountMinor { get; init; }

    public long TaxMinor { get; init; }

    /// <summary>
    /// What is left to tax: subtotal less every discount. Kept alongside
    /// <see cref="SubtotalMinor"/> and <see cref="TaxMinor"/> so a client can render an explicit
    /// subtotal/discount/net/tax/total breakdown without reconstructing the middle figure itself.
    /// </summary>
    public long NetSubtotalMinor { get; init; }

    /// <summary>
    /// The price's own configured tax, applied to what is due now -- null when the price carries
    /// no tax at all, present (with <see cref="SubscriptionPreviewTaxResponse.AmountMinor"/>
    /// possibly zero) whenever it does. A card-free trial due nothing today still has a taxed
    /// price; reporting nothing here for that case would read as "this price is untaxed," which
    /// is not true the moment money is actually due.
    /// </summary>
    public SubscriptionPreviewTaxResponse? Tax { get; init; }

    /// <summary>
    /// What confirming this preview would actually charge. Zero for a card-free trial, and for
    /// any subscription discounted to nothing.
    /// </summary>
    public long TotalDueNowMinor { get; init; }

    /// <summary>Whether the opening period is a fraction of a full one.</summary>
    public bool Prorated { get; init; }

    public int? CoveredDays { get; init; }

    public int? TotalDays { get; init; }

    public DateTime PeriodStartUtc { get; init; }

    public DateTime PeriodEndUtc { get; init; }

    /// <summary>
    /// When the next charge falls — the trial's end for a card-free trial, the end of an annual
    /// term collected with checkout, otherwise this period's own end. A boundary that merely opens
    /// an already-paid annual term is deliberately not reported as a renewal.
    /// </summary>
    public DateTime? NextRenewalAtUtc { get; init; }

    /// <summary>
    /// What a full, un-prorated recurring period costs, once the trial (if any) no longer
    /// applies. Unchanged in meaning by the addition of <see cref="NextCharge"/> below: this is
    /// never the shorter, prorated stub a calendar-aligned trial converts into mid-month, even
    /// though that stub is what the subscriber is actually charged next -- see
    /// <see cref="NextCharge"/> for that.
    /// </summary>
    public long NextRenewalAmountMinor { get; init; }

    /// <summary>
    /// The full breakdown behind <see cref="NextRenewalAmountMinor"/> -- built from the exact same
    /// full-period <c>PeriodCharge</c> that figure is read from, so the two can never disagree.
    /// Never null: every subscription this response describes has a next-renewal amount, even a
    /// zero one. Describes the same full, un-prorated period as <see cref="NextRenewalAmountMinor"/>
    /// -- see <see cref="NextCharge"/> for what is actually charged next when the two differ.
    /// </summary>
    public SubscriptionPreviewRenewalResponse NextRenewal { get; init; } = new();

    /// <summary>
    /// The charge actually due next -- which, for a subscription with no trial pending
    /// conversion, is the exact same full period <see cref="NextRenewal"/> already describes, but
    /// which for a calendar-aligned trial converting mid-month is the shorter, prorated stub the
    /// conversion actually buys, not the full period that follows it. Additive, and never null:
    /// check <see cref="SubscriptionPreviewNextChargeResponse.Prorated"/> to tell the two cases
    /// apart, since the amount alone cannot.
    /// </summary>
    public SubscriptionPreviewNextChargeResponse NextCharge { get; init; } = new();

    /// <summary>Set only for a subscription that opens on a trial.</summary>
    public DateTime? TrialEndsAtUtc { get; init; }

    /// <summary>
    /// Whether confirming this preview will ask for a card even though nothing is due now.
    /// </summary>
    public bool RequiresCardSetup { get; init; }

    /// <summary>
    /// The year a calendar-aligned yearly signup has also bought, mid-month, in addition to its
    /// stub. Null for every other case.
    /// </summary>
    public SubscriptionPreviewAnnualPeriodResponse? PendingAnnualPeriod { get; init; }

    /// <summary>
    /// Why this quote is temporary, when the discount code applied is a platform campaign rather
    /// than an ordinary promotional code. Null for a Standard discount and for no discount at all.
    /// </summary>
    /// <remarks>
    /// Every other field on this response already carries the right numbers for a campaign — the
    /// same pricing pipeline prices one exactly as it prices an ordinary code. What is missing
    /// without this is the "why": a buyer reading <see cref="TotalDueNowMinor"/> as zero, or
    /// <see cref="NextRenewalAmountMinor"/> as a smaller figure than the price's own list amount,
    /// has no way to tell from the numbers alone that either is temporary rather than the price
    /// they will keep paying.
    /// </remarks>
    public SubscriptionPreviewCampaignResponse? Campaign { get; init; }

    /// <summary>
    /// What would stop the confirm from succeeding, named rather than hidden behind a price the
    /// customer is then refused. Empty when nothing stands in the way.
    /// </summary>
    public List<SubscriptionPreviewBlockerResponse> Blockers { get; init; } = [];

    /// <summary>The instant these figures were derived from.</summary>
    public DateTime QuotedAtUtc { get; init; }

    /// <summary>
    /// The earliest instant at which this quote's proration could no longer hold — the next local
    /// midnight in the request's own time zone. Null when nothing here is prorated, because then
    /// no boundary changes the answer.
    /// </summary>
    public DateTime? QuoteValidUntilUtc { get; init; }
}

/// <summary>
/// A price's configured tax, applied to one specific amount -- the opening payment on the parent
/// response, or the next renewal on <see cref="SubscriptionPreviewRenewalResponse"/>.
/// </summary>
public sealed class SubscriptionPreviewTaxResponse
{
    public int RateBasisPoints { get; init; }

    /// <summary>"Inclusive" or "Exclusive" -- see <see cref="Services.SubscriptionTaxPresentation"/>.</summary>
    public string Mode { get; init; } = string.Empty;

    public long AmountMinor { get; init; }
}

/// <summary>
/// What a full renewal period costs once proration and any trial no longer apply -- the same
/// subtotal/discount/net/tax/total shape <see cref="SubscriptionPreviewResponse"/> itself uses
/// for the opening payment, so a client renders both with one component.
/// </summary>
public sealed class SubscriptionPreviewRenewalResponse
{
    public long SubtotalMinor { get; init; }

    public long BuiltInDiscountMinor { get; init; }

    public long PromotionalDiscountMinor { get; init; }

    public long DiscountMinor { get; init; }

    public long NetSubtotalMinor { get; init; }

    /// <summary>Null when the price carries no tax at all.</summary>
    public SubscriptionPreviewTaxResponse? Tax { get; init; }

    /// <summary>Equal to <see cref="SubscriptionPreviewResponse.NextRenewalAmountMinor"/>.</summary>
    public long TotalMinor { get; init; }

    /// <summary>Equal to <see cref="SubscriptionPreviewResponse.NextRenewalAtUtc"/>.</summary>
    public DateTime? RenewalAtUtc { get; init; }
}

/// <summary>
/// The charge actually due next, and the period it covers -- which can be a shorter, prorated
/// stub than <see cref="SubscriptionPreviewRenewalResponse"/>'s full period, for a calendar-
/// aligned trial converting mid-month.
/// </summary>
public sealed class SubscriptionPreviewNextChargeResponse
{
    /// <summary>When this charge actually happens -- the trial's own end, for a converting trial.</summary>
    public DateTime ChargeAtUtc { get; init; }

    public DateTime PeriodStartUtc { get; init; }

    public DateTime PeriodEndUtc { get; init; }

    /// <summary>
    /// Whether this charge covers a fraction of a full period rather than all of one -- true for
    /// a calendar-aligned trial ending mid-month, false everywhere else (including a trial that
    /// happens to end exactly on a calendar boundary, which has no stub to prorate).
    /// </summary>
    public bool Prorated { get; init; }

    /// <summary>Calendar dates this charge covers. Null unless <see cref="Prorated"/> is true.</summary>
    public int? CoveredDays { get; init; }

    /// <summary>Dates in the month <see cref="CoveredDays"/> is a fraction of. Null unless
    /// <see cref="Prorated"/> is true.</summary>
    public int? TotalDays { get; init; }

    public long SubtotalMinor { get; init; }

    public long BuiltInDiscountMinor { get; init; }

    public long PromotionalDiscountMinor { get; init; }

    public long DiscountMinor { get; init; }

    public long NetSubtotalMinor { get; init; }

    /// <summary>Null when the price carries no tax at all.</summary>
    public SubscriptionPreviewTaxResponse? Tax { get; init; }

    public long TotalMinor { get; init; }
}

public sealed class SubscriptionPreviewAnnualPeriodResponse
{
    public DateTime StartUtc { get; init; }

    public DateTime EndUtc { get; init; }

    public long AmountMinor { get; init; }

    public long NetAmountMinor { get; init; }

    public long TaxAmountMinor { get; init; }

    /// <summary>Whether the year's amount is already included in <c>totalDueNowMinor</c>.</summary>
    public bool CollectedWithCheckout { get; init; }
}

/// <summary>
/// The buyer-facing explanation for a campaign discount: what it is, and when the price it quoted
/// stops applying.
/// </summary>
public sealed class SubscriptionPreviewCampaignResponse
{
    /// <summary>
    /// <c>FreeOpeningCalendarPeriod</c> or <c>FirstAnnualPeriod</c> -- named rather than left for a
    /// client to infer from the numbers, so a client that wants its own wording still knows which
    /// campaign it is looking at.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>A short, ready-to-display sentence explaining the offer and when it ends.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The instant standard pricing resumes -- the opening stub's end for a free calendar month,
    /// or the discounted year's end for a first-annual-period campaign.
    /// </summary>
    public DateTime DiscountEndsAtUtc { get; init; }

    /// <summary>
    /// The entitlement a free-opening-period campaign temporarily caps, and the cap itself. Null
    /// for a campaign that carries no override, and always null for a first-annual-period
    /// campaign, which never carries one.
    /// </summary>
    public string? TemporaryEntitlementKey { get; init; }

    public long? TemporaryEntitlementLimit { get; init; }
}

/// <summary>
/// One reason confirming this preview would be refused.
/// </summary>
/// <remarks>
/// The same error codes <c>POST /subscriptions</c> itself returns, so a client already handling
/// those does not learn a second vocabulary for the preview.
/// </remarks>
public sealed class SubscriptionPreviewBlockerResponse
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    /// <summary>Set only for <c>subscription_billing_profile_incomplete</c>.</summary>
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }
}
