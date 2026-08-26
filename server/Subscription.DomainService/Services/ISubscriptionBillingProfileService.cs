using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads and writes the identity an organization's financial documents are addressed to.
/// </summary>
public interface ISubscriptionBillingProfileService
{
    /// <param name="requestedOrganizationId">
    /// An organization named by the request. Honoured only for the console, on the same policy as
    /// every other subscription read.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionBillingProfileResponse>> GetAsync(
        string? requestedOrganizationId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionBillingProfileResponse>> UpdateAsync(
        UpdateBillingProfileRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
