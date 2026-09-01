using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface IUsageRecordingService
{
    /// <summary>
    /// Records usage against a meter and reports where that leaves the allowance.
    /// </summary>
    /// <remarks>
    /// This, not the entitlement endpoint, is the enforcement point. The balance it returns
    /// already includes the caller's own contribution, so two callers arriving at the boundary
    /// together get different answers.
    /// </remarks>
    Task<SubscriptionOperationResult<UsageResponse>> RecordAsync(
        RecordUsageRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<IReadOnlyList<UsageResponse>>> GetCurrentUsageAsync(
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same current usage, with the choice of source and the diagnostics describing it.
    /// </summary>
    /// <remarks>
    /// Added beside <see cref="GetCurrentUsageAsync"/> rather than replacing it, so the existing
    /// signature and everything calling it keep working unchanged.
    /// </remarks>
    Task<SubscriptionOperationResult<UsageCurrentRead>> ReadCurrentAsync(
        string? organizationId,
        UsageReadMode readMode,
        string correlationId,
        CancellationToken cancellationToken);
}
