namespace Subscription.DomainService.Responses;

/// <summary>
/// What moving a live subscription to another plan or price would cost right now, without
/// applying anything.
/// </summary>
/// <remarks>
/// Every figure here comes from the exact same call to
/// <c>SubscriptionProrationCalculator.Calculate</c> that <c>ChargeAndApplyAsync</c> makes when the
/// change is actually confirmed — there is one arithmetic, evaluated a moment later. Unlike
/// <see cref="SubscriptionPreviewResponse"/>, nothing here is frozen ahead of time: a plan change
/// is priced fresh, immediately before it is applied, every time it runs, so this quote holds only
/// up to the clock — the same guarantee the quantity-change preview already makes. There is no
/// <c>quoteValidUntilUtc</c> because there is no discrete boundary to name: the proration is
/// continuous (ticks remaining over the period, not whole calendar days), so the figure drifts by
/// a small amount every instant that passes rather than jumping at a fixed point.
/// </remarks>
public sealed class SubscriptionPlanChangePreviewResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    public string TargetPlanCode { get; init; } = string.Empty;

    public string TargetPlanName { get; init; } = string.Empty;

    public string TargetPriceId { get; init; } = string.Empty;

    public string Interval { get; init; } = string.Empty;

    public int IntervalCount { get; init; }

    public List<SubscriptionQuantityResponse> Quantities { get; init; } = [];

    /// <summary>What confirming this preview would charge now. Zero for a downgrade.</summary>
    public long ChargeMinor { get; init; }

    /// <summary>
    /// What confirming this preview would bank as credit toward future renewals. Zero for an
    /// upgrade — a downgrade is never refunded, only credited.
    /// </summary>
    public long CreditBankedMinor { get; init; }

    /// <summary>The two priced sides — what is left of the old plan, what is bought of the new one.</summary>
    public FinancialDocumentSettlementResponse Settlement { get; init; } = new();

    public DateTime NewPeriodStartUtc { get; init; }

    public DateTime NewPeriodEndUtc { get; init; }

    /// <summary>What a full period costs at the target plan and price, once this change has settled.</summary>
    public long NextRenewalAmountMinor { get; init; }

    /// <summary>
    /// What would stop the confirm from succeeding, named rather than hidden behind a price the
    /// customer is then refused. Empty when nothing stands in the way.
    /// </summary>
    public List<SubscriptionPreviewBlockerResponse> Blockers { get; init; } = [];

    /// <summary>The instant these figures were derived from.</summary>
    public DateTime QuotedAtUtc { get; init; }
}
