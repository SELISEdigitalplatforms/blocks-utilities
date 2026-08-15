using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A meter's rate bands for one currency.
/// </summary>
/// <remarks>
/// A list rather than a currency-keyed map: BSON keys carry escaping rules a currency code does
/// not need, and the rate table is always read whole.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class MeterRateTable
{
    public string CurrencyCode { get; set; } = string.Empty;

    public List<MeterTier> Tiers { get; set; } = [];
}
