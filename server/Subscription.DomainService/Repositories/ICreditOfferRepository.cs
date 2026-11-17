using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>Standalone credit offers, one per meter per scope. See spec 005.</summary>
public interface ICreditOfferRepository
{
    /// <summary>False when an offer for that meter already exists at that scope.</summary>
    Task<bool> TryCreateAsync(CreditOffer offer, CancellationToken cancellationToken);

    /// <summary>
    /// The offer a buying organization is served for one meter: its own override when it has one,
    /// otherwise the tenant's. Disabled offers are returned too, so a caller can say why.
    /// </summary>
    Task<CreditOffer?> ResolveAsync(
        string tenantId,
        string? organizationId,
        string meterKey,
        CancellationToken cancellationToken);

    Task<CreditOffer?> FindAtScopeAsync(
        string tenantId,
        string? organizationId,
        string meterKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditOffer>> ListAsync(
        string tenantId,
        string? organizationId,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateAsync(CreditOffer offer, long expectedVersion, CancellationToken cancellationToken);
}
