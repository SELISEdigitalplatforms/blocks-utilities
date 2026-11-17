using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Requests;

/// <summary>
/// The terms of a standalone credit offer. The same shape creates one and edits one; on an edit the
/// meter key and organization come from the stored offer. See spec 005.
/// </summary>
public sealed class CreditOfferRequest
{
    /// <summary>
    /// The organization whose override this is. Honoured only for the platform console; omitted,
    /// the console authors the tenant-wide offer. Ignored on an edit.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>A meter resolvable in the catalogue at this scope. Ignored on an edit.</summary>
    public string MeterKey { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<MeterTopUpPriceRequest> UnitPrices { get; set; } = [];

    public decimal MinQuantity { get; set; } = 1;

    public decimal MaxQuantity { get; set; }

    /// <summary>A fixed lifetime from purchase, or null for credits that never expire.</summary>
    public CreditExpiryDurationRequest? ExpiresAfter { get; set; }

    public int? TaxRateBasisPoints { get; set; }

    public TaxMode? TaxMode { get; set; }

    public CreditSpendOrder SpendOrder { get; set; } = CreditSpendOrder.SubscriptionFirst;

    /// <summary>Fixed-size bundles at their own price. Replaced whole on every edit.</summary>
    public List<CreditPackRequest> Packs { get; set; } = [];
}

/// <summary>One pack of a standalone credit offer.</summary>
public sealed class CreditPackRequest
{
    /// <summary>Letters, digits, dashes and underscores; unique within the offer.</summary>
    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whole credits.</summary>
    public decimal Quantity { get; set; }

    public List<MeterTopUpPriceRequest> UnitPrices { get; set; } = [];
}
