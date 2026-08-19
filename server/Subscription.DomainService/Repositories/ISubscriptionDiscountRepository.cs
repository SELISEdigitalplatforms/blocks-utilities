using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionDiscountRepository
{
    Task<bool> TryCreateAsync(Discount discount, CancellationToken cancellationToken);
    Task<Discount?> FindActiveByCodeAsync(string tenantId, string? organizationId, string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<Discount>> ListAsync(string tenantId, string? organizationId, CancellationToken cancellationToken);
    Task<bool> TryArchiveAsync(string tenantId, string discountId, CancellationToken cancellationToken);
}
