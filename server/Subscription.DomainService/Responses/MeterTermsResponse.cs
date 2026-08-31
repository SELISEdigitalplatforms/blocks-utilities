namespace Subscription.DomainService.Responses;

/// <summary>
/// One meter's terms as the subscriber actually bought them.
/// </summary>
/// <remarks>
/// Read from <c>SubscriptionDetail.Plan.Meters</c> -- the subscription's own snapshot, taken at
/// signup -- never from the mutable plan catalogue. An edit to the catalogue after this
/// subscription was sold must not change what this reports, for the same reason it must not
/// change what period-end usage rating eventually charges. See
/// <c>SubscriptionUsageOveragePreviewService</c>'s remarks for the same principle applied to a
/// hypothetical charge instead of the terms themselves.
/// </remarks>
public sealed class MeterTermsResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    /// <summary>How much this meter includes per period, or for the subscription's lifetime when
    /// <see cref="ResetPolicy"/> is <c>"Never"</c>.</summary>
    public long IncludedQuantity { get; init; }

    /// <summary>"Periodic", "Never", or "CarryForward".</summary>
    public string ResetPolicy { get; init; } = string.Empty;

    /// <summary>The most that may roll into one window under <c>CarryForward</c>. Null otherwise.</summary>
    public long? CarryForwardCap { get; init; }

    /// <summary>Whether usage past the included quantity is permitted and billed at all.</summary>
    public bool OverageAllowed { get; init; }

    /// <summary>
    /// What overage costs in this subscription's own currency, or null. Null covers two distinct
    /// cases a client must be able to tell apart from <see cref="OverageAllowed"/> alone:
    /// overage is blocked outright, or overage is allowed but this plan defines no rate table for
    /// the subscription's currency (or that table's amounts could not be resolved to a major-unit
    /// figure -- see <see cref="Utilities.MinorUnitMajorAmountFormatter"/>). Either way, nothing
    /// here is a chargeable price; use <c>POST /api/subscription-usage/overage/preview</c> for an
    /// exact quote.
    /// </summary>
    public OveragePricingResponse? OveragePricing { get; init; }
}

/// <summary>A meter's graduated overage rates, already converted to the subscription's currency.</summary>
public sealed class OveragePricingResponse
{
    public string CurrencyCode { get; init; } = string.Empty;

    public List<OverageTierResponse> Tiers { get; init; } = [];
}

/// <summary>
/// One graduated tier band, priced in major units -- e.g. <c>"1.00"</c> CHF, <c>"100"</c> JPY,
/// <c>"0.100"</c> KWD. An invariant decimal string, not a number: presented for display, not for
/// arithmetic, and not the internal minor-unit representation billing actually rates from.
/// </summary>
public sealed class OverageTierResponse
{
    /// <summary>Upper bound of the band, counted in overage units. Null is the final, unbounded tier.</summary>
    public long? UpToQuantity { get; init; }

    public string UnitAmount { get; init; } = string.Empty;
}
