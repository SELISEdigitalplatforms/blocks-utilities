using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IPlanResponseMapper
{
    /// <param name="hasSubscribers">
    /// Whether anything has ever subscribed to this plan, which is what decides whether it may
    /// still be edited. Defaulted for the paths that have not asked — a plan just created has no
    /// subscriber by definition.
    /// </param>
    /// <param name="predecessorDisplayName">
    /// The resolved name of <see cref="Plan.PredecessorPlanId"/>, so the mapper does not have to
    /// look it up itself. Null when there is no predecessor, or it no longer resolves.
    /// </param>
    /// <param name="successorPlanId">
    /// The plan that named this one as its predecessor, if the caller resolved one. Left null by
    /// every path that has not looked (e.g. building a list) — absence here does not mean no
    /// successor exists, only that nobody checked.
    /// </param>
    /// <param name="successorDisplayName">The resolved name of <paramref name="successorPlanId"/>.</param>
    PlanResponse ToResponse(
        Plan plan,
        IReadOnlyList<Price> prices,
        bool hasSubscribers = false,
        string? predecessorDisplayName = null,
        string? successorPlanId = null,
        string? successorDisplayName = null);
}
