using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// What a plan costs, in one currency, on one cadence.
/// </summary>
/// <remarks>
/// Prices are authored per currency, never converted. A rate-converted price moves every month,
/// which makes invoices unstable and refunds lossy; and nobody sells at 92.47 anyway.
/// <para>
/// Amounts are minor units in a <see cref="long"/> — 8900 is CHF 89.00, and 8900 is also
/// JPY 8900. A currency is meaningless without its exponent, so the two always travel together.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class Price
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;

    public string PlanId { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = string.Empty;

    public long UnitAmountMinor { get; set; }

    public BillingInterval Interval { get; set; } = BillingInterval.Month;

    public int IntervalCount { get; set; } = 1;

    /// <summary>
    /// Which quantity item this price charges for. Null is a flat fee that does not multiply.
    /// </summary>
    public string? QuantityItemKey { get; set; }

    /// <summary>
    /// Tax to add on top, in basis points out of 10,000 — the same idiom
    /// <see cref="DiscountTerms.PercentBasisPoints"/> uses. Null means not taxable. Authored by
    /// whoever sets up the price, the same way its currency is: manual, not jurisdiction-derived.
    /// </summary>
    public int? TaxRateBasisPoints { get; set; }

    public CatalogueStatus Status { get; set; } = CatalogueStatus.Draft;

    public List<ProviderPriceMirror> ProviderMirrors { get; set; } = [];

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
