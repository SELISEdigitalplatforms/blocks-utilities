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

    Task<SubscriptionOperationResult<IReadOnlyList<UsageResponse>>> GetCurrentUsageAsync(
        string correlationId,
        CancellationToken cancellationToken);
}
