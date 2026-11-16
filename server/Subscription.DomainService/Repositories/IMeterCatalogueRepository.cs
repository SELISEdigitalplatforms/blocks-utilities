using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>The tenant's meter catalogue, with per-organization overrides. See spec 005.</summary>
public interface IMeterCatalogueRepository
{
    /// <summary>False when the key already exists at that scope.</summary>
    Task<bool> TryCreateAsync(MeterCatalogueEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// The entry for one key as seen from one organization: its own override when it has one,
    /// otherwise the tenant's. Null for the tenant scope reads the tenant entry alone.
    /// </summary>
    Task<MeterCatalogueEntry?> ResolveAsync(
        string tenantId,
        string? organizationId,
        string meterKey,
        CancellationToken cancellationToken);

    /// <summary>The entry at exactly one scope, overrides not consulted.</summary>
    Task<MeterCatalogueEntry?> FindAtScopeAsync(
        string tenantId,
        string? organizationId,
        string meterKey,
        CancellationToken cancellationToken);

    /// <summary>Tenant entries plus the organization's own, or tenant entries alone for null.</summary>
    Task<IReadOnlyList<MeterCatalogueEntry>> ListAsync(
        string tenantId,
        string? organizationId,
        CancellationToken cancellationToken);

    /// <summary>Guarded by version; false when the entry moved on or is gone.</summary>
    Task<bool> TryUpdateAsync(MeterCatalogueEntry entry, long expectedVersion, CancellationToken cancellationToken);
}
