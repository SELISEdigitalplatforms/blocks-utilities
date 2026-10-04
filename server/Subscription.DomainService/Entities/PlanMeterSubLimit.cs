using MongoDB.Bson.Serialization.Attributes;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Entities;

/// <summary>
/// One cap on how fast a meter's allowance may be spent: so much per window.
/// </summary>
/// <remarks>
/// A meter may carry several — "1,000 every five hours and 20,000 a week" — and a use has to fit
/// every one of them as well as the period's allowance. Each counts on its own counter, so two
/// limits on one meter never share a figure.
/// <para>
/// The meaning of each field is the one the single pace always had; see <see cref="PlanMeter"/>'s
/// legacy fields for the history of each.
/// </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlanMeterSubLimit
{
    public UsageWindow Window { get; set; }

    /// <summary>How many of <see cref="Window"/> the limit spans. At least one.</summary>
    public int WindowCount { get; set; } = 1;

    /// <summary>Looks back from now rather than counting within a block on the clock.</summary>
    public bool Rolling { get; set; }

    /// <summary>How much may be used within one span.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Whether going past it refuses the use or only reports it.</summary>
    public MeterSubLimitBehaviour Behaviour { get; set; } = MeterSubLimitBehaviour.Refuse;

    /// <summary>
    /// The limit's length in hours, which is how two limits are compared: a day and twenty-four
    /// hours are the same length whichever unit they were written in.
    /// </summary>
    public int SpanHours => Math.Max(1, WindowCount) * Window switch
    {
        UsageWindow.Day => 24,
        UsageWindow.Week => 24 * 7,
        _ => 1
    };
}
