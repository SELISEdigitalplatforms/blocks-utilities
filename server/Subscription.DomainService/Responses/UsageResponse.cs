namespace Subscription.DomainService.Responses;

/// <summary>
/// The state of one meter after a usage call.
/// </summary>
public sealed class UsageResponse
{
    /// <summary>
    /// Whether the caller may proceed. Reflects the balance <em>including</em> this call, which
    /// is what makes it an answer rather than an estimate.
    /// </summary>
    public bool Allowed { get; init; }

    public string MeterKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public string PeriodKey { get; init; } = string.Empty;

    public DateTime PeriodStartUtc { get; init; }

    public DateTime PeriodEndUtc { get; init; }

    public long Included { get; init; }

    public long Used { get; init; }

    /// <summary>How much of the allowance is left. Never below zero.</summary>
    public long Remaining { get; init; }

    /// <summary>How much has been used beyond the allowance.</summary>
    public long Overage { get; init; }

    /// <summary>True when this call repeated one already recorded and changed nothing.</summary>
    public bool Replayed { get; init; }
}
