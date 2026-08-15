using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IEntitlementService
{
    /// <summary>
    /// Everything the caller's organization is entitled to.
    /// </summary>
    /// <param name="fresh">
    /// Bypass the short-lived cache. Worth setting before something irreversible.
    /// </param>
    Task<SubscriptionOperationResult<EntitlementSnapshotResponse>> GetAsync(
        bool fresh,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<EntitlementResponse>> GetAsync(
        string entitlementKey,
        bool fresh,
        string correlationId,
        CancellationToken cancellationToken);
}
