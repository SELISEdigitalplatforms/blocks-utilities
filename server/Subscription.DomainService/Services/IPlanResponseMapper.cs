using Subscription.DomainService.Entities;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IPlanResponseMapper
{
    PlanResponse ToResponse(Plan plan, IReadOnlyList<Price> prices);
}
