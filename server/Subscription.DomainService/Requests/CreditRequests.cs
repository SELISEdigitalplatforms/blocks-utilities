namespace Subscription.DomainService.Requests;

/// <summary>A quote for, or a purchase of, credits for one meter. See spec 002.</summary>
public sealed class CreditPurchaseRequest
{
    /// <summary>Honoured only for the platform console, like every other subscription request.</summary>
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Whole credits, within the meter's minimum and maximum purchase.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The seat to buy for, on a seated subscription. Null buys for the shared pool.</summary>
    public int? SeatNumber { get; set; }

    /// <summary>A credit-purchase discount code. Optional.</summary>
    public string? DiscountCode { get; set; }

    /// <summary>
    /// Required to buy, ignored by a preview. The same key and the same request return the first
    /// result without charging again; the same key with a different request is refused.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>Credits given without a charge.</summary>
public sealed class GrantCreditsRequest
{
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    /// <summary>Whole credits.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Null grants to the shared pool.</summary>
    public int? SeatNumber { get; set; }

    /// <summary>Why the credits were given. Required, and kept on the grant and in the audit trail.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Credits moved from one bucket of a seated subscription to another.</summary>
public sealed class MoveCreditsRequest
{
    public string? OrganizationId { get; set; }

    public string MeterKey { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    /// <summary>The seat to take from. Null takes from the shared pool.</summary>
    public int? FromSeatNumber { get; set; }

    /// <summary>The seat to give to. Null gives to the shared pool.</summary>
    public int? ToSeatNumber { get; set; }
}
