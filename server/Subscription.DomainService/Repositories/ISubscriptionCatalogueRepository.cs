using Subscription.DomainService.Entities;

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

    Task<IReadOnlyList<Plan>> ListPlansAsync(
        string tenantId,
        string? organizationId,
        CancellationToken cancellationToken);

    Task<bool> TryUpdatePlanAsync(
        string tenantId,
        string planId,
        int expectedVersion,
        Plan plan,
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

    Task<bool> TrySetPriceMirrorAsync(
        string tenantId,
        string priceId,
        ProviderPriceMirror mirror,
        CancellationToken cancellationToken);
}
