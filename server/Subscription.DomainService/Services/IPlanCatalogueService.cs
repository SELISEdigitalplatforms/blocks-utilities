using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IPlanCatalogueService
{
    Task<SubscriptionOperationResult<PlanResponse>> CreatePlanAsync(
        CreatePlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites what a plan sells, leaving its code, scope and prices where they are.
    /// </summary>
    /// <remarks>
    /// Refused with <c>subscription_plan_in_use</c> once anything has subscribed. Subscribing
    /// copies the plan's terms onto the subscription and bills from that copy, so editing a plan
    /// that was sold cannot reach the people already on it — it would leave the catalogue saying
    /// one thing and every live subscription another. A plan nobody has bought has no such
    /// history, which is the only case this allows.
    /// </remarks>
    Task<SubscriptionOperationResult<PlanResponse>> UpdatePlanAsync(
        string planId,
        UpdatePlanRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a price off the menu. Answers with the plan as it now stands.
    /// </summary>
    /// <remarks>
    /// Nobody already subscribed is affected: a subscription bills from the price snapshot
    /// copied onto it and never reads the catalogue again. What stops is selling it — a new
    /// subscription or a plan change naming an archived price is refused.
    /// </remarks>
    Task<SubscriptionOperationResult<PlanResponse>> ArchivePriceAsync(
        string priceId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<PlanResponse>> CreatePriceAsync(
        CreatePriceRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<PlanResponse>> UpdatePriceTaxAsync(
        string priceId,
        UpdatePriceTaxRequest request,
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
