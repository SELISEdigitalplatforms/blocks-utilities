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
