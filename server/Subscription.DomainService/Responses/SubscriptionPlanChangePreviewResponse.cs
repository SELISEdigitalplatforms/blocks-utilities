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
    /// <c>Immediate</c> or <c>NextRenewal</c> — when confirming this preview would take effect.
    /// </summary>
    /// <remarks>
    /// A change worth more than what it replaces hands something over now and is charged for now.
    /// One worth the same or less takes something away, and taking it away before the paid period
    /// runs out would be a refund by another name, so it waits. A trial is always immediate: it has
    /// paid for nothing, so it has no paid period to protect.
    /// </remarks>
    public string Timing { get; init; } = string.Empty;

    /// <summary>
    /// When the change would actually take effect. Now, for an immediate change; the end of the
    /// period already paid for, for a scheduled one.
    /// </summary>
    public DateTime EffectiveAtUtc { get; init; }

    /// <summary>
    /// Always zero. Deprecated.
    /// </summary>
    /// <remarks>
    /// A downgrade used to bank the unused time on the plan being left as credit toward future
    /// renewals. It no longer does: a downgrade is scheduled for the end of the period already
    /// paid for, so the subscriber keeps what they bought and there is nothing unused to hand
    /// back. Nothing else banks credit either, so no response can carry a non-zero value here.
    /// <para>
    /// Kept rather than removed so a client that already reads it keeps deserializing. Credit the
    /// subscriber already holds is unaffected and is still spent against an immediate upgrade —
    /// that shows up as <c>settlement.creditConsumedMinor</c>, which is a different figure and
    /// always was.
    /// </para>
    /// </remarks>
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
