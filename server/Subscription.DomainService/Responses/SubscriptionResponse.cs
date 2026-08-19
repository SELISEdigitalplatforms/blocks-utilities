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
