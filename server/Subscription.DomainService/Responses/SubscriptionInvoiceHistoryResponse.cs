namespace Subscription.DomainService.Responses;

public sealed class SubscriptionInvoiceHistoryResponse
{
    public IReadOnlyList<SubscriptionInvoiceHistoryItemResponse> Items { get; init; } = [];

    public SubscriptionInvoiceHistoryPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class SubscriptionInvoiceHistoryItemResponse
{
    public string PaymentDetailId { get; init; } = string.Empty;

    public string? SubscriptionId { get; init; }

    public string InvoiceType { get; init; } = string.Empty;

    public string? PeriodKey { get; init; }

    public string ProviderName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public decimal RefundedAmount { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime IssuedAtUtc { get; init; }

    public long? NetAmountMinor { get; init; }

    public long? TaxAmountMinor { get; init; }

    public long? CreditAmountMinor { get; init; }

    public int? TaxRateBasisPoints { get; init; }

    public string? TaxMode { get; init; }

    /// <summary>
    /// What this invoice was made of before tax. Null together on a plan-change or quantity
    /// settlement, which reports <see cref="Settlement"/> instead — its amount is the difference
    /// between two prorated periods and has two sides rather than one. Also null on a first charge (a
    /// hosted checkout composes no invoice) and on payments raised before the breakdown existed.
    /// </summary>
    /// <remarks>
    /// Reported separately rather than as one reduction, because "CHF 13.00 came off" cannot be
    /// turned back into "the price gave 8%, the band gave 5%, the coupon gave nothing" — and that is
    /// the question somebody reading a months-old invoice is actually asking.
    /// </remarks>
    public long? GrossAmountMinor { get; init; }

    /// <summary>What the price's automatic discount and the volume band took off between them.</summary>
    public long? BuiltInDiscountMinor { get; init; }

    /// <summary>What a promotional code took off after that.</summary>
    public long? PromotionalDiscountMinor { get; init; }

    /// <summary>What was left to tax: gross less both reductions above.</summary>
    public long? DiscountedAmountMinor { get; init; }

    /// <summary>Basis points the price took off automatically. Null when it had none.</summary>
    public int? AutomaticDiscountBasisPoints { get; init; }

    /// <summary>The band's rate, when the quantity selected one.</summary>
    public int? QuantityDiscountBasisPoints { get; init; }

    /// <summary>
    /// "BestDiscount" or "Additive" — how the two authored reductions met, so a reader can tell a
    /// charge that combined them from one that chose between them.
    /// </summary>
    public string? QuantityDiscountCombination { get; init; }

    /// <summary>
    /// How a settlement's amount was arrived at, on a plan-change or quantity invoice. Null on a
    /// renewal, which the flat fields above describe.
    /// </summary>
    /// <remarks>
    /// A settlement is a subtraction, so it is reported as one: the period being left, the period
    /// being joined, and what closed the gap. A subscriber charged mid-month is asking about the two
    /// sides, not the remainder.
    /// </remarks>
    public SubscriptionSettlementResponse? Settlement { get; init; }

    /// <summary>
    /// An authenticated application endpoint, never Stripe's bearer-style document URL.
    /// </summary>
    public string DownloadUrl { get; init; } = string.Empty;
}

/// <summary>Both sides of a settlement, and what closed the gap between them.</summary>
public sealed class SubscriptionSettlementResponse
{
    /// <summary>The period the subscriber left part-way through.</summary>
    public SubscriptionSettlementSideResponse Outgoing { get; init; } = new();

    /// <summary>The period they joined.</summary>
    public SubscriptionSettlementSideResponse Target { get; init; } = new();

    /// <summary>Banked credit spent against the difference.</summary>
    public long CreditConsumedMinor { get; init; }

    /// <summary>
    /// Target prorated value less outgoing unused value less credit. Negative where a downgrade
    /// banked credit rather than charging, which is why it is not simply the amount charged.
    /// </summary>
    public long NetSettlementMinor { get; init; }
}

/// <summary>One side of a settlement, priced as its own period and then prorated.</summary>
public sealed class SubscriptionSettlementSideResponse
{
    public long GrossAmountMinor { get; init; }

    /// <summary>The price's automatic discount and the volume band, combined as that price says.</summary>
    public long BuiltInDiscountMinor { get; init; }

    /// <summary>A promotional code, after the built-in reduction was settled.</summary>
    public long PromotionalDiscountMinor { get; init; }

    /// <summary>Tax at this side's own rate and mode — a change can cross between the two.</summary>
    public long TaxAmountMinor { get; init; }

    /// <summary>The whole period, tax included.</summary>
    public long PeriodTotalMinor { get; init; }

    /// <summary>
    /// The part of that this settlement counted: unused time on the outgoing side, remaining time on
    /// the target side.
    /// </summary>
    public long ProratedValueMinor { get; init; }

    /// <summary>What was taxed: gross less both reductions above.</summary>
    public long DiscountedAmountMinor { get; init; }
}

public sealed class SubscriptionInvoiceHistoryPageInfoResponse
{
    public int PageSize { get; init; }

    public bool HasNextPage { get; init; }

    public string? NextCursor { get; init; }
}
