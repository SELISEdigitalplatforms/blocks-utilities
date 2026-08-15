namespace Subscription.DomainService.Requests;

public sealed class CreateSubscriptionRequest
{
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>
    /// Which price to buy at. The currency comes from this price and is fixed for the life of
    /// the subscription.
    /// </summary>
    public string PriceId { get; set; } = string.Empty;

    public List<SubscriptionQuantityRequest> Quantities { get; set; } = [];

    /// <summary>
    /// The customer's own time zone, as an IANA identifier. Their calendar decides when a
    /// period turns over, not the server's.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    public string? DiscountCode { get; set; }

    /// <summary>
    /// Notably absent: the organization. It comes from the authenticated caller, because
    /// accepting it here would let anyone subscribe — or read — on another organization's
    /// behalf by naming it.
    /// </summary>
    public string? BillingEmail { get; set; }

    public string? BillingName { get; set; }
}

public sealed class SubscriptionQuantityRequest
{
    public string ItemKey { get; set; } = string.Empty;

    public long Quantity { get; set; }
}
