using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The pricing an offline subscription was activated on. See spec 006.
/// </summary>
/// <remarks>
/// A record of what the operator chose, not the amount itself. A contract amount is also written
/// into the subscription's <see cref="PriceSnapshot"/> at activation, so every calculator that
/// already prices a renewal, a proration or a preview reads it without knowing contracts exist.
/// This is kept beside it so a response, a report and a later re-pricing can tell a contract
/// figure apart from a catalogue one that happens to be equal.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class OfflinePricing
{
    public OfflinePricingMode Mode { get; set; }

    /// <summary>Set only for <see cref="OfflinePricingMode.Contract"/>: the flat net amount per interval.</summary>
    public long? ContractAmountMinor { get; set; }

    /// <summary>Set only for <see cref="OfflinePricingMode.Contract"/>.</summary>
    public string? ContractCurrencyCode { get; set; }
}
