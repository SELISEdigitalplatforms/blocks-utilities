using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A named thing the plan counts.
/// </summary>
/// <remarks>
/// The key is the product's word — "screening" for one client, "envelope" for another — and the
/// platform treats it as an opaque name throughout. Nothing here knows what is being counted,
/// which is what lets a second product be onboarded as configuration.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanMeter
{
    public string MeterKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string UnitLabel { get; set; } = string.Empty;

    public MeterAggregation Aggregation { get; set; } = MeterAggregation.Sum;

    /// <summary>Periodic by default; Never keeps one lifetime balance across renewals.</summary>
    public MeterResetPolicy ResetPolicy { get; set; } = MeterResetPolicy.Periodic;

    /// <summary>How much the plan includes per period, or for its lifetime when reset is Never.</summary>
    public long IncludedQuantity { get; set; }

    /// <summary>
    /// The most that may roll into one window under
    /// <see cref="MeterResetPolicy.CarryForward"/>. Required for that policy, null for the others.
    /// </summary>
    /// <remarks>
    /// Bounds the amount carried in, not the total, so the plan's own included quantity is always
    /// available on top of it. Required because an unbounded roll is almost never what was sold: a
    /// subscription that consumes nothing for a year would otherwise arrive at month thirteen
    /// holding twelve months of quota.
    /// </remarks>
    public long? CarryForwardCap { get; set; }

    /// <summary>Whether usage past the included quantity is permitted and billed.</summary>
    public bool OverageAllowed { get; set; } = true;

    /// <summary>
    /// Percentages of the included quantity that raise an event when first crossed, such as
    /// 80 and 100. Each fires once per period.
    /// </summary>
    public List<int> ThresholdPercents { get; set; } = [];

    public List<MeterRateTable> RateTables { get; set; } = [];
}
