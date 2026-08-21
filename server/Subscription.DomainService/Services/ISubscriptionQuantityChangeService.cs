using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public interface ISubscriptionQuantityChangeService
{
    /// <summary>
    /// What a quantity change would cost and when it would take effect, mutating nothing.
    /// </summary>
    /// <remarks>
    /// Runs the same validation and the same arithmetic as
    /// <see cref="ChangeAsync"/> against the same version, so a caller that confirms what it was
    /// shown gets what it was quoted.
    /// </remarks>
    Task<SubscriptionOperationResult<QuantityChangeResponse>> PreviewAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a quantity change: an increase now, a decrease at the end of the paid period.
    /// </summary>
    Task<SubscriptionOperationResult<QuantityChangeResponse>> ChangeAsync(
        string subscriptionId,
        ChangeQuantityRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Withdraws a scheduled decrease, leaving the current quantity in place.</summary>
    Task<SubscriptionOperationResult<QuantityChangeResponse>> CancelPendingAsync(
        string subscriptionId,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);
}
