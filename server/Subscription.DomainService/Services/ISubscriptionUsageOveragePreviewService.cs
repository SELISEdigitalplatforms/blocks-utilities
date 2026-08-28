using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Prices a hypothetical slice of additional metered usage, without recording or charging
/// anything.
/// </summary>
public interface ISubscriptionUsageOveragePreviewService
{
    Task<SubscriptionOperationResult<SubscriptionUsageOveragePreviewResponse>> PreviewAsync(
        PreviewUsageOverageRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
