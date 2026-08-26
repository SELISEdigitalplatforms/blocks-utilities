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
    /// Where this price's renewal boundary falls: the subscriber's anniversary, or the first of
    /// the calendar month.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="Enums.BillingAlignment.Anniversary"/>, which is both the enum's zero
    /// and how every price authored before alignment existed was sold — so an existing document
    /// deserializes to exactly the behaviour it already had.
    /// </remarks>
    public BillingAlignment BillingAlignment { get; set; } = BillingAlignment.Anniversary;

    /// <summary>
    /// The monthly price a calendar-aligned <em>yearly</em> price prices its opening stub from.
    /// </summary>
    /// <remarks>
    /// Required for a calendar-aligned yearly price and refused on every other price. A subscriber
    /// joining on 25 August owes a week, and a week of an annual price is not a meaningful
    /// quantity — what they owe is a week of the monthly equivalent, which has to be named rather
    /// than guessed at by dividing the annual figure by twelve.
    /// <para>
    /// It prices the stub and nothing else. <see cref="UnitAmountMinor"/> stays independently
    /// authored, because what a year costs is a commercial decision — an annual plan is usually
    /// not twelve monthly ones.
    /// </para>
    /// </remarks>
    public string? CalendarStubBasePriceId { get; set; }

    /// <summary>
    /// When a calendar-aligned yearly price collects its annual amount: at the boundary the year
    /// begins, or up front alongside the stub.
    /// </summary>
    /// <remarks>
    /// Only meaningful on a calendar-aligned yearly price, and refused elsewhere. Defaults to
    /// <see cref="Enums.CalendarAnnualChargeTiming.AtBoundary"/>, the enum's zero and the more
    /// conservative reading: a year nobody has started is a year nobody has paid for.
    /// </remarks>
    public CalendarAnnualChargeTiming CalendarAnnualChargeTiming { get; set; } =
        CalendarAnnualChargeTiming.AtBoundary;

    /// <summary>
    /// The linked monthly price's unit amount, copied at authoring time.
    /// </summary>
    /// <remarks>
    /// Stored here as well as on the linked price so a stub can be priced without a second
    /// catalogue read, and so retiring or repricing the monthly price later cannot change what an
    /// annual price is derived from.
    /// </remarks>
    public long? CalendarStubBaseUnitAmountMinor { get; set; }

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
