using Subscription.DomainService.Enums;
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

    /// <summary>
    /// Takes a whole plan off the menu, permanently.
    /// </summary>
    /// <remarks>
    /// Nobody already subscribed is affected, and none of their prices is touched: a subscription
    /// bills from the plan and price snapshot copied onto it when it was sold and never reads the
    /// catalogue again. Renewal, usage rating, entitlements, invoicing and cancellation all
    /// continue exactly as before. What stops is selling — a new subscription, a purchase preview
    /// or a plan change naming this plan is refused with <c>subscription_plan_archived</c>, and so
    /// is every further change to the plan or its prices.
    /// <para>
    /// There is no restore. A replacement is made by duplicating the plan, which is why the
    /// archived plan stays fully readable.
    /// </para>
    /// <para>
    /// Idempotent: archiving an already-archived plan returns it unchanged, without a second
    /// write. This is the one place the plan differs from <see cref="ArchivePriceAsync"/>, which
    /// reports a repeat as a conflict — a price is retired one of several on a live plan, where
    /// repeating the call usually means the caller has lost track of which, whereas archiving a
    /// plan twice is the same request arriving twice and has one sensible answer.
    /// </para>
    /// <para>
    /// Refused with <c>subscription_plan_changed</c> when an unrelated edit lands between reading
    /// the plan and archiving it, rather than archiving terms nobody reviewed. A plan still in
    /// <c>Draft</c> is not archivable and answers as not found, consistent with a draft appearing
    /// in no catalogue view.
    /// </para>
    /// </remarks>
    Task<SubscriptionOperationResult<PlanResponse>> ArchivePlanAsync(
        string planId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<PlanResponse>> CreatePriceAsync(
        CreatePriceRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets or clears a price's automatic discount. Answers with the plan as it now stands.
    /// </summary>
    /// <remarks>
    /// Future-facing, like the tax editor beside it: a new subscription and a plan change onto this
    /// price snapshot the new figure, and every existing subscriber keeps the one they were sold.
    /// </remarks>
    Task<SubscriptionOperationResult<PlanResponse>> UpdatePriceDiscountAsync(
        string priceId,
        UpdatePriceDiscountRequest request,
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
    /// <param name="filter">
    /// Which plans to return. Defaults to <see cref="PlanCatalogueFilter.Active"/>, so every
    /// subscriber-facing caller that omits it keeps receiving only what is on sale — which is what
    /// keeps archived plans out of subscribe and change-plan selectors without those screens
    /// having to filter anything themselves.
    /// </param>
    Task<SubscriptionOperationResult<IReadOnlyList<PlanResponse>>> ListPlansAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken,
        PlanCatalogueFilter filter = PlanCatalogueFilter.Active);

    Task<SubscriptionOperationResult<PlanResponse>> GetPlanAsync(
        string planId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
