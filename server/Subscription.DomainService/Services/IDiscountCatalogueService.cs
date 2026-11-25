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

    /// <summary>
    /// Who created or changed this discount, and when, newest first. <c>limit</c> defaults to 100,
    /// at most 500. Not found under the same rule as <see cref="GetAsync"/>.
    /// </summary>
    Task<SubscriptionOperationResult<IReadOnlyList<SubscriptionAuditEventResponse>>> GetAuditAsync(
        string discountId, string? organizationId, int limit, string correlationId, CancellationToken cancellationToken);
}
