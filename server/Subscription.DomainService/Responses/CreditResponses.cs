namespace Subscription.DomainService.Responses;

/// <summary>What a credit purchase would cost, computed exactly as the purchase computes it.</summary>
public sealed class CreditQuoteResponse
{
    public string MeterKey { get; init; } = string.Empty;

    public int? SeatNumber { get; init; }

    public decimal Quantity { get; init; }

    public string CurrencyCode { get; init; } = string.Empty;

    public long UnitAmountMinor { get; init; }

    /// <summary>Quantity times the unit price.</summary>
    public long GrossMinor { get; init; }

    public long DiscountMinor { get; init; }

    /// <summary>What is taxed.</summary>
    public long NetMinor { get; init; }

    public long TaxMinor { get; init; }

    /// <summary>What the stored card is charged.</summary>
    public long TotalMinor { get; init; }

    public int? TaxRateBasisPoints { get; init; }

    public string? TaxMode { get; init; }

    /// <summary>The code applied, normalized. Null when none was.</summary>
    public string? DiscountCode { get; init; }

    /// <summary>"Periodic", "CarryForward" or "Never".</summary>
    public string ExpiryPolicy { get; init; } = string.Empty;

    /// <summary>
    /// When the credits stop being usable, for credits that expire with the current usage window.
    /// Shown before buying, so a purchase late in a window is never a surprise.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>
    /// "StoredCard" when the saved card is charged during the request, or "Checkout" when the
    /// billing account has no card and the buyer is sent to the provider's page to pay (spec 003).
    /// </summary>
    public string PaymentPath { get; init; } = nameof(Enums.CreditPaymentPath.StoredCard);
}

/// <summary>One grant of credits, with what is left of it.</summary>
public sealed class CreditGrantResponse
{
    public string GrantId { get; init; } = string.Empty;

    public string MeterKey { get; init; } = string.Empty;

    /// <summary>Null for the shared pool.</summary>
    public int? SeatNumber { get; init; }

    /// <summary>"Purchased", "AdminGranted" or "Moved".</summary>
    public string Source { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal Remaining { get; init; }

    public string ExpiryPolicy { get; init; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>"Active", "Exhausted", "Expired" or "Forfeited".</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime GrantedAtUtc { get; init; }

    public string GrantedByUserId { get; init; } = string.Empty;

    public string? Reason { get; init; }

    public string? PurchaseId { get; init; }

    public string? SourceGrantId { get; init; }
}

/// <summary>A credit purchase and, once it has been granted, the grant it produced.</summary>
/// <remarks>
/// The invoice is not here: it is issued in the background once the payment settles, and appears in
/// the subscription's invoice list like every other charge's.
/// </remarks>
public sealed class CreditPurchaseResponse
{
    public string PurchaseId { get; init; } = string.Empty;

    /// <summary>"Pending", "AwaitingPayment", "Charged", "Finalized", "Failed" or "NeedsAttention".</summary>
    public string Status { get; init; } = string.Empty;

    public CreditQuoteResponse Quote { get; init; } = new();

    public string? PaymentId { get; init; }

    /// <summary>
    /// Where the buyer pays, while the purchase is "AwaitingPayment". Credits are granted once the
    /// provider confirms the payment, never on the buyer's return; resubmit the same idempotency
    /// key to see how it ended.
    /// </summary>
    public string? CheckoutUrl { get; init; }

    public CreditGrantResponse? Grant { get; init; }

    public string? FailureCode { get; init; }

    public string? FailureReason { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>True when this answers a retry with the original result, without charging again.</summary>
    public bool Replayed { get; init; }
}

/// <summary>What a move took from and gave to.</summary>
public sealed class CreditMoveResponse
{
    /// <summary>The grants credits were taken from, as they stand afterwards.</summary>
    public List<CreditGrantResponse> From { get; init; } = [];

    /// <summary>The grants created in the receiving bucket, one per grant drawn on.</summary>
    public List<CreditGrantResponse> To { get; init; } = [];
}
