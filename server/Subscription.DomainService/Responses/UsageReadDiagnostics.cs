using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Responses;

/// <summary>
/// How a current-usage read was served. Never part of the usage data itself.
/// </summary>
/// <remarks>
/// Returned beside the response body rather than inside it, so both read modes keep the identical
/// <see cref="UsageResponse"/> array contract and no existing consumer sees a changed shape.
/// </remarks>
public sealed class UsageReadDiagnostics
{
    /// <summary>What the caller asked for.</summary>
    public UsageReadMode RequestedMode { get; init; }

    /// <summary>
    /// What actually answered. Differs from <see cref="RequestedMode"/> when a projection read found
    /// nothing and fell back to the counters.
    /// </summary>
    public UsageReadMode ActualMode { get; init; }

    public double DurationMs { get; init; }

    /// <summary>How many meter-windows were returned.</summary>
    public int DocumentCount { get; init; }

    /// <summary>
    /// How old the freshest projected document in the answer is. Null for an authoritative read,
    /// which has no projection age, and for a projection read that returned nothing.
    /// </summary>
    public double? NewestProjectionAgeSeconds { get; init; }

    /// <summary>
    /// True when at least one document in the answer is older than the configured staleness
    /// threshold.
    /// </summary>
    /// <remarks>
    /// Age alone, not version lag: comparing every document's <c>sourceVersion</c> against its
    /// counter would mean reading the counters, which is the work this mode exists to avoid. Version
    /// lag is what the reconciliation worker checks, where reading both is the job.
    /// </remarks>
    public bool Stale { get; init; }
}
