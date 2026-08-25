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
    /// What this invoice was made of before tax. Null together on a first charge (a hosted checkout
    /// composes no invoice), on a plan-change or quantity settlement (the amount there is the
    /// difference between two prorated periods, which does not decompose into a gross and a
    /// reduction), and on payments raised before the breakdown was recorded.
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
    /// An authenticated application endpoint, never Stripe's bearer-style document URL.
    /// </summary>
    public string DownloadUrl { get; init; } = string.Empty;
}

public sealed class SubscriptionInvoiceHistoryPageInfoResponse
{
    public int PageSize { get; init; }

    public bool HasNextPage { get; init; }

    public string? NextCursor { get; init; }
}
