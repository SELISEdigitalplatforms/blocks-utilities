using MongoDB.Bson.Serialization.Attributes;

namespace Subscription.DomainService.Entities;

/// <summary>
/// The plan's terms as they stood when the subscription was created, copied rather than
/// referenced.
/// </summary>
/// <remarks>
/// Two things follow, and both are wanted. Entitlement becomes a single document read, with no
/// join to a catalogue that may since have moved. And editing a plan stops being retroactive:
/// a subscriber keeps what they were sold until something deliberately migrates them, which is
/// the correct billing semantic as well as the fast one.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanSnapshot
{
    public string PlanId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? FeaturesJson { get; set; }

    public List<PlanEntitlement> Entitlements { get; set; } = [];

    public List<PlanMeter> Meters { get; set; } = [];

    public List<PlanQuantityItem> QuantityItems { get; set; } = [];

    /// <summary>The plan version this was taken from, so a migration can tell what is stale.</summary>
    public int PlanVersion { get; set; }

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}
