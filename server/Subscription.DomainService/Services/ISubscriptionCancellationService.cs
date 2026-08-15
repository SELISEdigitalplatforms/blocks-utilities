using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface ISubscriptionCancellationService
{
    /// <summary>
    /// Records that a subscription should end.
    /// </summary>
    /// <param name="immediately">
    /// End now rather than at the end of the period already paid for. Reserved for cases where
    /// the customer is entitled to stop at once; the ordinary answer is false.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionResponse>> CancelAsync(
        string subscriptionId,
        bool immediately,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken);
}
