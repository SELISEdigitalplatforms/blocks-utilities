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

    public string? DisplayPriceNote { get; set; }

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

    /// <summary>
    /// Whether <see cref="TaxRateBasisPoints"/> is added to the amount or already inside it.
    /// </summary>
    /// <remarks>
    /// Null on records authored before modes existed, and read as
    /// <see cref="Enums.TaxMode.Exclusive"/> — the behaviour those prices were sold on. Ignored
    /// entirely when there is no rate.
    /// </remarks>
    public TaxMode? TaxMode { get; set; }

    /// <summary>
    /// A percentage taken off this price automatically, in basis points out of 10,000 — 800 is 8%.
    /// Null or zero is no automatic discount.
    /// </summary>
    /// <remarks>
    /// On the price rather than the plan because it is a cadence-specific offer: "8% for paying
    /// yearly" is a property of the yearly price, and the monthly price beside it under the same plan
    /// has none. Applied without a code, to every charge this price produces, for as long as the
    /// subscription stays on it.
    /// </remarks>
    public int? AutomaticDiscountBasisPoints { get; set; }

    /// <summary>
    /// How <see cref="AutomaticDiscountBasisPoints"/> meets the volume band a subscriber's quantity
    /// selects. Null reads as <see cref="Enums.AutomaticDiscountCombination.BestDiscount"/>.
    /// </summary>
    public AutomaticDiscountCombination? QuantityDiscountCombination { get; set; }


    public CatalogueStatus Status { get; set; } = CatalogueStatus.Draft;

    public List<ProviderPriceMirror> ProviderMirrors { get; set; } = [];

    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedDateUtc { get; set; } = DateTime.UtcNow;
}
