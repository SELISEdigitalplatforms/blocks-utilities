namespace Subscription.DomainService.Responses;

public sealed class SubscriptionFinancialDocumentHistoryResponse
{
    public IReadOnlyList<SubscriptionFinancialDocumentResponse> Items { get; init; } = [];

    public SubscriptionFinancialDocumentPageInfoResponse PageInfo { get; init; } = new();
}

public sealed class SubscriptionFinancialDocumentPageInfoResponse
{
    public int PageSize { get; init; }

    public bool HasNextPage { get; init; }

    public string? NextCursor { get; init; }
}

/// <summary>
/// One issued document, as a client sees it.
/// </summary>
/// <remarks>
/// Everything a list or a detail view needs without a second call, including the party snapshots — a
/// client rendering "billed to" must show what the document says, not what the profile says now, or
/// the page and the PDF will disagree.
/// </remarks>
public sealed class SubscriptionFinancialDocumentResponse
{
    public string DocumentId { get; init; } = string.Empty;

    public string DocumentNumber { get; init; } = string.Empty;

    public string DocumentType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime IssuedAtUtc { get; init; }

    public string SubscriptionId { get; init; } = string.Empty;

    public string CurrencyCode { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public DateTime? PeriodStartUtc { get; init; }

    public DateTime? PeriodEndUtc { get; init; }

    public string? PeriodLocalStart { get; init; }

    public string? PeriodLocalEnd { get; init; }

    public string? TimeZoneId { get; init; }

    /// <summary>Every figure on the document, in minor units.</summary>
    public FinancialDocumentAmountsResponse Amounts { get; init; } = new();

    /// <summary>
    /// The two sides of a plan or quantity change. Null on a renewal, whose amounts describe it in
    /// full.
    /// </summary>
    public FinancialDocumentSettlementResponse? Settlement { get; init; }

    public IReadOnlyList<FinancialDocumentLineResponse> Lines { get; init; } = [];

    public FinancialDocumentTrialResponse? Trial { get; init; }

    public string SubscriberLegalName { get; init; } = string.Empty;

    public string BillingContactName { get; init; } = string.Empty;

    public string? BillingContactEmail { get; init; }

    public string InitiatedByName { get; init; } = string.Empty;

    public string? InitiatedByUserId { get; init; }

    /// <summary>The payment this invoice was issued for. Null on a trial invoice.</summary>
    public string? PaymentDetailId { get; init; }

    /// <summary>The refund a credit note documents, where it came from one.</summary>
    public string? RefundId { get; init; }

    public string? OriginalDocumentId { get; init; }

    public string? OriginalDocumentNumber { get; init; }

    /// <summary>
    /// Whether the PDF exists yet, so a client can show a download control without probing for a 404.
    /// </summary>
    public bool IsPdfAvailable { get; init; }

    /// <summary>
    /// Whether every delivery attempt has been spent.
    /// </summary>
    /// <remarks>
    /// Not the same question as <see cref="IsPdfAvailable"/>, and a client must not treat it that
    /// way: delivery is abandoned when the PDF could never be rendered <em>or</em> when the PDF
    /// exists but the mail could not be confirmed sent -- <see cref="LastErrorCode"/> tells the two
    /// apart. A document with a PDF and an abandoned mail is still fully downloadable; it is the
    /// email, not the document, that needs a person.
    /// </remarks>
    public bool IsAbandoned { get; init; }

    /// <summary>
    /// The classification of the last delivery failure, so a client can tell a render/storage
    /// failure (safe to retry -- nothing has been mailed) from a mail failure, and tell an address
    /// that was simply never on file (<c>document_no_recipient</c>, harmless to resend) from a
    /// publish whose outcome could not be established (<c>document_mail_outcome_unknown</c>, where
    /// a blind resend risks a subscriber receiving the same invoice twice). Null once delivered, or
    /// on a document that has not failed anything yet.
    /// </summary>
    public string? LastErrorCode { get; init; }

    /// <summary>SHA-256 of the stored PDF, for a client that wants to verify what it downloaded.</summary>
    public string? PdfContentHash { get; init; }

    public string DownloadUrl { get; init; } = string.Empty;
}

public sealed class FinancialDocumentAmountsResponse
{
    public long GrossSubtotalMinor { get; init; }

    public long AutomaticDiscountMinor { get; init; }

    public long QuantityDiscountMinor { get; init; }

    public long PromotionalDiscountMinor { get; init; }

    public long NetSubtotalMinor { get; init; }

    public int? TaxRateBasisPoints { get; init; }

    public string? TaxMode { get; init; }

    public long TaxAmountMinor { get; init; }

    public long CreditAppliedMinor { get; init; }

    public long TotalMinor { get; init; }

    public int? AutomaticDiscountBasisPoints { get; init; }

    public int? QuantityDiscountBasisPoints { get; init; }

    public string? DiscountCombination { get; init; }

    public string? PromotionCode { get; init; }
}

public sealed class FinancialDocumentSettlementResponse
{
    public FinancialDocumentSettlementSideResponse Outgoing { get; init; } = new();

    public FinancialDocumentSettlementSideResponse Target { get; init; } = new();

    public long CreditConsumedMinor { get; init; }

    public long NetSettlementMinor { get; init; }

    /// <summary>
    /// The prepaid annual period's own settlement, alongside this one, when a change taken during
    /// a calendar-aligned yearly subscription's opening stub settled both together. Null for every
    /// ordinary settlement.
    /// </summary>
    public FinancialDocumentSettlementResponse? Annual { get; init; }
}

public sealed class FinancialDocumentSettlementSideResponse
{
    public long GrossAmountMinor { get; init; }

    public long BuiltInDiscountMinor { get; init; }

    public long PromotionalDiscountMinor { get; init; }

    public long TaxAmountMinor { get; init; }

    public long PeriodTotalMinor { get; init; }

    public long ProratedValueMinor { get; init; }
}

public sealed class FinancialDocumentLineResponse
{
    public string Description { get; init; } = string.Empty;

    public long? Quantity { get; init; }

    public long? UnitAmountMinor { get; init; }

    public long AmountMinor { get; init; }

    public string? ItemKey { get; init; }
}

public sealed class FinancialDocumentTrialResponse
{
    public DateTime StartsAtUtc { get; init; }

    public DateTime EndsAtUtc { get; init; }

    public bool RequiresPaymentMethod { get; init; }

    public DateTime? FirstBillingAtUtc { get; init; }
}
