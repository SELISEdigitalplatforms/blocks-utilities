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

    public decimal Included { get; init; }

    public decimal Used { get; init; }

    /// <summary>How much of the allowance is left. Never below zero.</summary>
    public decimal Remaining { get; init; }

    /// <summary>How much has been used beyond the allowance.</summary>
    public decimal Overage { get; init; }

    /// <summary>True when this call repeated one already recorded and changed nothing.</summary>
    public bool Replayed { get; init; }

    /// <summary>
    /// Whether the read model describing this meter was published before this response was returned.
    /// </summary>
    /// <remarks>
    /// Additive and purely diagnostic. The usage in this response is authoritative whatever this says:
    /// the counter recorded it, and a read model that could fail a committed billing write would be an
    /// authority rather than a projection of one. A caller that does not read the projection can
    /// ignore this field entirely.
    /// <para>
    /// <c>Pending</c> means the usage is recorded but the projection could not be written, and a
    /// repair has been scheduled. A consumer reading the projection directly will see the previous
    /// figure until that repair runs.
    /// </para>
    /// </remarks>
    public UsageProjectionState Projection { get; init; } = UsageProjectionState.Published;
}

/// <summary>The projection's state at the moment a usage response was produced.</summary>
public enum UsageProjectionState
{
    /// <summary>Published, or already ahead of this call because a later recording won the race.</summary>
    Published = 0,

    /// <summary>Not written; a repair is scheduled. The usage itself is recorded.</summary>
    Pending = 1
}
