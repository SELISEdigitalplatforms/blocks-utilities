using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>Moves a live subscription to a different price, mid-period, with proration.</summary>
public interface ISubscriptionPlanChangeService
{
    Task<SubscriptionOperationResult<SubscriptionResponse>> ChangePlanAsync(
        string subscriptionId,
        ChangeSubscriptionPlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
