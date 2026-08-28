using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IDiscountCatalogueService
{
    Task<SubscriptionOperationResult<DiscountResponse>> CreateAsync(CreateDiscountRequest request, string correlationId, CancellationToken cancellationToken);
    Task<SubscriptionOperationResult<DiscountResponse>> GetAsync(string discountId, string? organizationId, string correlationId, CancellationToken cancellationToken);
    Task<SubscriptionOperationResult<DiscountResponse>> UpdateAsync(string discountId, UpdateDiscountRequest request, string? organizationId, string correlationId, CancellationToken cancellationToken);
    Task<SubscriptionOperationResult<IReadOnlyList<DiscountResponse>>> ListAsync(string? organizationId, string correlationId, CancellationToken cancellationToken);
    Task<SubscriptionOperationResult<DiscountResponse>> ArchiveAsync(string discountId, string? organizationId, string correlationId, CancellationToken cancellationToken);
}
