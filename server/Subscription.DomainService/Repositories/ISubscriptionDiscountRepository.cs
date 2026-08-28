using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionDiscountRepository
{
    Task<bool> TryCreateAsync(Discount discount, CancellationToken cancellationToken);
    Task<Discount?> FindActiveByCodeAsync(string tenantId, string? organizationId, string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<Discount>> ListAsync(string tenantId, string? organizationId, CancellationToken cancellationToken);
    Task<bool> TryArchiveAsync(string tenantId, string discountId, CancellationToken cancellationToken);

    /// <summary>Scoped the same way every other lookup here is: a discount outside this tenant is unknown, never forbidden.</summary>
    Task<Discount?> FindByIdAsync(string tenantId, string discountId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a discount's editable fields, atomically checking <paramref name="expectedVersion"/>
    /// against the stored value and incrementing it on success.
    /// </summary>
    /// <remarks>
    /// One update, filtered on the version rather than read-modify-write: two admins editing the
    /// same campaign a moment apart must not silently overwrite each other, and a filter checked
    /// in the database is the only way that is actually true rather than merely likely.
    /// </remarks>
    /// <returns>
    /// True if the version matched and the write applied. False either because the discount does
    /// not exist in this scope, or because the version had already moved -- the caller
    /// distinguishes the two with a fresh <see cref="FindByIdAsync"/>.
    /// </returns>
    Task<bool> TryUpdateAsync(Discount discount, long expectedVersion, CancellationToken cancellationToken);
}
