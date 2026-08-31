using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionCatalogueRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    Task<bool> TryCreatePlanAsync(Plan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a plan by its code, preferring the organization's own over the tenant's.
    /// </summary>
    /// <remarks>
    /// The same fallback payment configuration uses. Plans are sold to organizations by the
    /// tenant, so requiring one per organization would mean copying every plan for every
    /// customer; an organization that needs its own terms can still have them.
    /// </remarks>
    Task<Plan?> FindPlanByCodeAsync(
        string tenantId,
        string? organizationId,
        string code,
        CancellationToken cancellationToken);

    Task<Plan?> GetPlanAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken);

    /// <param name="filter">
    /// Which plans to return, and whether to resolve them. <see cref="PlanCatalogueFilter.Active"/>
    /// collapses an organization's plan over the tenant's of the same code, because that is what
    /// subscribing resolves and a list that showed both would offer a choice it cannot honour. The
    /// archived views deliberately do not collapse: a replacement sharing a code is usually the
    /// reason somebody is reading history.
    /// </param>
    Task<IReadOnlyList<Plan>> ListPlansAsync(
        string tenantId,
        string? organizationId,
        PlanCatalogueFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// The archived plan a caller would have resolved for <paramref name="code"/>, had it still
    /// been on sale.
    /// </summary>
    /// <remarks>
    /// Exists only so a refused sale can say why. It is called after
    /// <see cref="FindPlanByCodeAsync"/> has already returned nothing, never instead of it —
    /// resolving both statuses together would let an organization's archived plan shadow the
    /// tenant's active one of the same code and refuse a sale that should have gone through.
    /// <para>
    /// Follows the same organization-then-tenant visibility, so a plan belonging to an
    /// organization the caller cannot see stays invisible and the refusal stays a plain
    /// not-found rather than a hint that the code exists somewhere.
    /// </para>
    /// </remarks>
    Task<Plan?> FindArchivedPlanByCodeAsync(
        string tenantId,
        string? organizationId,
        string code,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds the plan that named <paramref name="predecessorPlanId"/> as its own predecessor, if
    /// one exists — the reverse of <see cref="Plan.PredecessorPlanId"/>. Unindexed: plan counts
    /// per tenant are small, the same assumption <see cref="ListPlansAsync"/> already makes for
    /// its own full scan, and this is only ever called once per single-plan read.
    /// </summary>
    Task<Plan?> FindSuccessorPlanAsync(
        string tenantId,
        string predecessorPlanId,
        CancellationToken cancellationToken);

    Task<bool> TryUpdatePlanAsync(
        string tenantId,
        string planId,
        int expectedVersion,
        Plan plan,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves an active plan to archived, if it is still active and still at
    /// <paramref name="expectedVersion"/>.
    /// </summary>
    /// <remarks>
    /// False covers three different situations the caller has to tell apart @D@ the plan is gone, it
    /// was archived by somebody else, or an unrelated edit moved its version on. The repository
    /// cannot distinguish them from a write result alone, so it reports only whether this call was
    /// the one that changed the document, and the caller re-reads to find out which.
    /// <para>
    /// Draft is not archivable through this path. A draft plan appears in no catalogue view, so
    /// there is nothing to take off a menu, and permitting it would put a plan into an
    /// irreversible state it was never sellable from.
    /// </para>
    /// </remarks>
    Task<bool> TryArchivePlanAsync(
        string tenantId,
        string planId,
        int expectedVersion,
        DateTime archivedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryCreatePriceAsync(Price price, CancellationToken cancellationToken);

    Task<Price?> GetPriceAsync(
        string tenantId,
        string priceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Price>> ListPricesAsync(
        string tenantId,
        string planId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a price off the menu without removing it.
    /// </summary>
    /// <remarks>
    /// Compare-and-set on Active, so archiving one already archived reports false rather than
    /// claiming to have changed something. Existing subscribers are untouched by design: they
    /// bill from the snapshot copied onto the subscription, and never read this row again.
    /// </remarks>
    Task<bool> TryArchivePriceAsync(
        string tenantId,
        string priceId,
        DateTime archivedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TryUpdatePriceTaxAsync(
        string tenantId,
        string priceId,
        int expectedVersion,
        int? taxRateBasisPoints,
        TaxMode? taxMode,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites a price's automatic discount, compare-and-set on its version.
    /// </summary>
    /// <remarks>
    /// Reaches future snapshots only. Anyone already subscribed holds their own copy of these two
    /// values and is charged from it, so clearing a discount here never repossesses one already sold.
    /// </remarks>
    Task<bool> TryUpdatePriceAutomaticDiscountAsync(
        string tenantId,
        string priceId,
        int expectedVersion,
        int? automaticDiscountBasisPoints,
        AutomaticDiscountCombination? combination,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> TrySetPriceMirrorAsync(
        string tenantId,
        string priceId,
        ProviderPriceMirror mirror,
        CancellationToken cancellationToken);
}
