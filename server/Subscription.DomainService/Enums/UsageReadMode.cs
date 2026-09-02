namespace Subscription.DomainService.Enums;

/// <summary>
/// Where a current-usage read gets its figures.
/// </summary>
/// <remarks>
/// A diagnostic and performance choice, not a correctness one: neither mode may be used to authorise
/// usage. Only <c>POST /api/subscription-usage</c> with <c>enforce</c> can claim capacity, because
/// only the counter's atomic increment settles a race between two callers wanting the same last unit.
/// </remarks>
public enum UsageReadMode
{
    /// <summary>
    /// The authoritative counters. The default, and unchanged from before this projection existed.
    /// </summary>
    /// <remarks>
    /// Default so that no existing caller's behaviour depends on a read model having been published.
    /// A projection that is briefly behind is acceptable for a dashboard; changing what the existing
    /// endpoint returns without being asked is not.
    /// </remarks>
    Authoritative = 0,

    /// <summary>
    /// The published projection, in one indexed query.
    /// </summary>
    /// <remarks>
    /// Falls back to the authoritative counters when the projection has nothing for this
    /// subscription, so a caller that opts in cannot be handed an empty allowance for a subscription
    /// that simply has not been published yet. The fallback is reported in the diagnostics rather
    /// than hidden.
    /// </remarks>
    Projection = 1
}
