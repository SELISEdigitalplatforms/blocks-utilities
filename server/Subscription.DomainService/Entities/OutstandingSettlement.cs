using Payment.DomainService.Entities;

namespace Subscription.DomainService.Entities;

/// <summary>
/// Money a subscription still owes for a change that was already applied: a renewal, plan change or
/// seat increase whose charge was accepted and then failed later (the provider's hold expired, was
/// cancelled, or the payment was declined after the fact).
/// </summary>
/// <remarks>
/// The change itself is never undone — spec 009. Instead the amount is kept here and the dunning
/// cycle charges it again in place of the period fee, until it settles or the attempts run out.
/// One per subscription: a second failure while one is open adds to it rather than queueing.
/// </remarks>
public sealed class OutstandingSettlement
{
    public string SettlementId { get; set; } = Guid.NewGuid().ToString("N");

    public long AmountMinor { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>The failed payment's own order id, reused so the retry is the same order at the provider.</summary>
    public string OrderId { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The payment that failed. For support, and so a watch for it is never raised twice.</summary>
    public string FailedPaymentDetailId { get; set; } = string.Empty;

    public DateTime FailedAtUtc { get; set; }

    /// <summary>
    /// Charges raised for this settlement so far. Part of each retry's idempotency key, and kept
    /// apart from <see cref="SubscriptionDetail.DunningAttemptCount"/> because that resets on
    /// Unpaid, which would replay a stale decline instead of charging a card added since.
    /// </summary>
    public int AttemptCount { get; set; }

    public long NetAmountMinor { get; set; }

    public long TaxAmountMinor { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public string? TaxMode { get; set; }

    public long GrossAmountMinor { get; set; }

    public long BuiltInDiscountMinor { get; set; }

    public long PromotionalDiscountMinor { get; set; }

    public int? AutomaticDiscountBasisPoints { get; set; }

    public int? QuantityDiscountBasisPoints { get; set; }

    public string? DiscountCombination { get; set; }

    public SubscriptionSettlementBreakdown? Settlement { get; set; }
}
