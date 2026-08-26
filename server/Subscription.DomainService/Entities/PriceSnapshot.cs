using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The price as sold, copied onto the subscription.
/// </summary>
/// <remarks>
/// A subscriber's price must not move because someone edited the catalogue. Repricing a plan
/// affects new subscriptions only.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PriceSnapshot
{
    public string PriceId { get; set; } = string.Empty;

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
    /// The monthly price this yearly price's opening stub was priced from, and that price's unit
    /// amount as it stood when this subscription was sold.
    /// </summary>
    /// <remarks>
    /// Snapshotted like everything else here, and for a sharper reason than most: the stub is
    /// charged at checkout and the annual period a month later, so a live catalogue read would let
    /// somebody editing the monthly price in between change what an annual subscriber already
    /// agreed to. Null on every price that is not a calendar-aligned yearly one.
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

    public long? CalendarStubBaseUnitAmountMinor { get; set; }

    public string? DisplayPriceNote { get; set; }

    public string? QuantityItemKey { get; set; }

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
    /// The automatic discount as sold, in basis points. Snapshotted for the same reason the
    /// amount is: editing the catalogue must not reprice anybody already subscribed.
    /// </summary>
    public int? AutomaticDiscountBasisPoints { get; set; }

    /// <summary>
    /// How the automatic discount met the volume band, as sold. Null reads as
    /// <see cref="Enums.AutomaticDiscountCombination.BestDiscount"/>.
    /// </summary>
    public AutomaticDiscountCombination? QuantityDiscountCombination { get; set; }

    public int PriceVersion { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
