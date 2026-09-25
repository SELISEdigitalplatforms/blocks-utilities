using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Giving out and taking back the seats a subscription was bought with.
/// </summary>
public interface ISubscriptionMemberService
{
    /// <summary>
    /// Puts people on a subscription — one, or all of them at once.
    /// </summary>
    /// <remarks>
    /// The whole call is refused when the subscription is not user-wise, grants nothing any more,
    /// or does not say how many people it is for. Past that the answer is per person: a batch
    /// cannot be atomic, since nothing here spans documents, so reporting one verdict for ten
    /// names would send an administrator looking for assignments that already landed.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionMemberAssignmentResponse>> AssignAsync(
        string subscriptionId,
        AssignMemberRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes a seat back, leaving what its holder spent on the subscription.
    /// </summary>
    /// <remarks>
    /// Usage counts against the subscription and is not reset, so whoever takes the seat next
    /// inherits the remainder of the window. That is the agreed behaviour, not an oversight: the
    /// organization bought a period's worth of allowance, and it does not renew because the person
    /// using it changed.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionMemberResponse>> ReleaseAsync(
        string subscriptionId,
        string userId,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>Who is holding this subscription's seats, and how many are left.</summary>
    Task<SubscriptionOperationResult<SubscriptionMembersResponse>> ListAsync(
        string subscriptionId,
        string correlationId,
        CancellationToken cancellationToken);
}
