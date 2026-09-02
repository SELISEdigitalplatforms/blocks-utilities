using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// How much of one meter a trial includes.
/// </summary>
/// <remarks>
/// Separate from the plan's own included quantity because a trial is not a free month. Where
/// each unit costs the seller real money, an uncapped trial is both a direct loss and an
/// obvious way in for anyone willing to sign up repeatedly.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class TrialMeterGrant
{
    public string MeterKey { get; set; } = string.Empty;

    public decimal IncludedQuantity { get; set; }
}
