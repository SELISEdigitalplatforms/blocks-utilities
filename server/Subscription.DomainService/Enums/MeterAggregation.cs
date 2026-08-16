namespace Subscription.DomainService.Enums;

/// <summary>
/// How a meter's entries combine into the figure a period is judged on.
/// </summary>
/// <remarks>
/// Phase 1 implements <see cref="Sum"/> only; the others are defined so the enum does not have
/// to widen later, and are refused explicitly rather than silently treated as a sum.
/// </remarks>
public enum MeterAggregation
{
    /// <summary>Entries add up. The ordinary "how many did they use" meter.</summary>
    Sum = 0,

    /// <summary>The highest value seen in the period, for peak-based pricing.</summary>
    Max = 1,

    /// <summary>The most recent value, for a gauge such as stored bytes.</summary>
    LastValue = 2
}
