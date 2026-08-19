using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// A reduction applied to this subscription's charges.
/// </summary>
/// <remarks>
/// Snapshotted from the discount catalogue and applied to signup, renewal and proration.
/// Percentages are basis points so a
/// third off is exact rather than 33.33 rounded somewhere unpredictable.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class DiscountTerms
{
    public string Code { get; set; } = string.Empty;

    public DiscountKind Kind { get; set; } = DiscountKind.Percent;

    /// <summary>Basis points off, when the kind is a percentage. 2500 is a quarter off.</summary>
    public int? PercentBasisPoints { get; set; }

    /// <summary>Minor units off, when the kind is a fixed amount.</summary>
    public long? AmountMinor { get; set; }

    /// <summary>How many periods it applies to. Null runs for the life of the subscription.</summary>
    public int? DurationPeriods { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
}
