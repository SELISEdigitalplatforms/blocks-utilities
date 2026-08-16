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
    PlanResponse ToResponse(
        Plan plan,
        IReadOnlyList<Price> prices,
        bool hasSubscribers = false);
}
