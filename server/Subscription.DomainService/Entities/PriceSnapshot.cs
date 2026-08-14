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

    public string? QuantityItemKey { get; set; }

    public int PriceVersion { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
