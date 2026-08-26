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
    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<EntitlementSnapshotResponse>> GetAsync(
        bool fresh,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<EntitlementResponse>> GetAsync(
        string entitlementKey,
        bool fresh,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
