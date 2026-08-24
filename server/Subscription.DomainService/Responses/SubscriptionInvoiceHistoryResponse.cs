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
