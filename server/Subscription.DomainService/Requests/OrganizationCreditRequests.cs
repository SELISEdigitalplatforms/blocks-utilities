namespace Subscription.DomainService.Requests;

/// <summary>A quote for, or a purchase of, an organization's standalone credits. See spec 005.</summary>
public sealed class OrganizationCreditPurchaseRequest
{
    /// <summary>Honoured only for the platform console, like every other subscription request.</summary>
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Whole credits, within the offer's minimum and maximum purchase.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Which of the offer's prices to pay. Required: an organization with no subscription has no
    /// currency of its own, and one with several grants bought in different currencies is fine,
    /// because a grant is credits, not money.
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>A credit-purchase discount code. Optional.</summary>
    public string? DiscountCode { get; set; }

    /// <summary>
    /// Required to buy, ignored by a preview. The same key and the same request return the first
    /// result without charging again; the same key with a different request is refused.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>Standalone credits given to an organization without a charge.</summary>
public sealed class OrganizationGrantCreditsRequest
{
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Whole credits.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Why the credits were given. Required, and kept on the grant and in the audit trail.</summary>
    public string Reason { get; set; } = string.Empty;
}
