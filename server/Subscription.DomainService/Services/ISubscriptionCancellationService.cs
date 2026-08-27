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
    /// <param name="organizationId">
    /// An organization named by the caller, if any. Trusted only for the platform console — see
    /// <see cref="Subscription.DomainService.Requests.CreateSubscriptionRequest.OrganizationId"/>
    /// for the full rule.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionResponse>> CancelAsync(
        string subscriptionId,
        bool immediately,
        string? reason,
        string? organizationId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Recovers usage-closure reservations left <c>CloseReserved</c> longer than their configured
    /// timeout — the crash window between a cancellation's own transition landing (or losing) and
    /// the commit-or-release call that should have followed it ever actually running.
    /// </summary>
    /// <returns>How many stale reservations were resolved (committed or released).</returns>
    Task<int> ReconcileStaleClosuresAsync(string tenantId, CancellationToken cancellationToken);
}
