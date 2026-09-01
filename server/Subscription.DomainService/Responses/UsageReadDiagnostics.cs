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
    /// What actually answered. Differs from <see cref="RequestedMode"/> whenever a projection read
    /// fell back — see <see cref="Fallback"/> for which kind of fallback it was.
    /// </summary>
    public UsageReadMode ActualMode { get; init; }

    /// <summary>
    /// Why the answer did not come from where it was asked for, if it did not.
    /// </summary>
    /// <remarks>
    /// Distinguished rather than collapsed into one "fell back", because the two say different things
    /// to an operator. Nothing published is a subscription this projection has never covered — a
    /// backfill matter. Some meters published is a projection that is actively incomplete, which means
    /// a publish or a seed was lost for a subscription the projection does cover, and that is worth
    /// looking at.
    /// </remarks>
    public UsageReadFallback Fallback { get; init; } = UsageReadFallback.None;

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

/// <summary>Why a current-usage read did not come from the source it was asked for.</summary>
public enum UsageReadFallback
{
    /// <summary>It did. The answer came from the requested source.</summary>
    None = 0,

    /// <summary>
    /// The projection held nothing at all for this subscription, so the counters answered.
    /// </summary>
    ProjectionEmpty = 1,

    /// <summary>
    /// The projection held some of the subscription's current windows but not all of them, so the
    /// counters answered for the whole request.
    /// </summary>
    /// <remarks>
    /// The whole request, not the missing part. Returning the published subset would silently omit
    /// meters the plan defines, and a caller drawing a usage screen from it would show fewer meters
    /// than the subscription has — with nothing in the response to say so. The two modes are required
    /// to return equivalent data, and a subset is not equivalent.
    /// </remarks>
    ProjectionPartial = 2
}
