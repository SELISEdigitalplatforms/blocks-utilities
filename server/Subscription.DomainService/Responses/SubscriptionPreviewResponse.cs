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
    /// When the next charge falls — the trial's end for a card-free trial, otherwise this
    /// period's own end.
    /// </summary>
    public DateTime? NextRenewalAtUtc { get; init; }

    /// <summary>What a full period costs once proration and the trial no longer apply.</summary>
    public long NextRenewalAmountMinor { get; init; }

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
