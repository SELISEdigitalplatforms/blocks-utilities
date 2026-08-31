namespace Subscription.DomainService.Enums;

/// <summary>
/// Which plans an administrative catalogue listing asks for.
/// </summary>
/// <remarks>
/// Not <see cref="CatalogueStatus"/>, though it reads like a subset of it. Two of these values are
/// not statuses at all: <see cref="All"/> spans two, and <see cref="Active"/> carries the
/// organization-over-tenant resolution that makes a listing match what subscribing would actually
/// find. Reusing the status enum here would invite a caller to ask for <c>Draft</c>, which no
/// catalogue view offers.
/// </remarks>
public enum PlanCatalogueFilter
{
    /// <summary>
    /// The sellable catalogue, resolved: an organization's own plan hides the tenant's of the same
    /// code, exactly as <c>FindPlanByCodeAsync</c> resolves it. The default, and what every
    /// subscriber-facing caller gets by omitting the parameter.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Every visible archived plan, uncollapsed. History is the point of this view, so a plan is
    /// never hidden because a replacement shares its code — that replacement is usually the reason
    /// somebody is looking.
    /// </summary>
    Archived = 1,

    /// <summary>
    /// The resolved Active catalogue plus every visible archived plan. Draft is excluded here as
    /// it is everywhere else: it is an internal state, and no catalogue view has ever shown it.
    /// </summary>
    All = 2
}
