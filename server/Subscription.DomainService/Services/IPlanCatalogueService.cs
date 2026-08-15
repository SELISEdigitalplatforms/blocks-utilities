using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IPlanCatalogueService
{
    Task<SubscriptionOperationResult<PlanResponse>> CreatePlanAsync(
        CreatePlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<PlanResponse>> CreatePriceAsync(
        CreatePriceRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<IReadOnlyList<PlanResponse>>> ListPlansAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<PlanResponse>> GetPlanAsync(
        string planId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
