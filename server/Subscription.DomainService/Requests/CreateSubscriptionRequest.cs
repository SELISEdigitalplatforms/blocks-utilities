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
    /// Which organization within the tenant this subscribes. Omit it to use the caller's own
    /// organization.
    /// </summary>
    /// <remarks>
    /// Ignored unless the caller is the platform console (<c>Payment:ConsoleOrganizationId</c>)
    /// — everyone else's own token organization is used regardless of what this carries, the
    /// same rule <see cref="Payment.DomainService.Requests.MakePaymentRequest.OrganizationId"/>
    /// already follows. Without this the console — which is fixed to one organization for every
    /// tenant — could only ever simulate a subscription for that one organization.
    /// </remarks>
    public string? OrganizationId { get; set; }

    public string? BillingEmail { get; set; }

    public string? BillingName { get; set; }
}

public sealed class SubscriptionQuantityRequest
{
    public string ItemKey { get; set; } = string.Empty;

    public long Quantity { get; set; }
}
