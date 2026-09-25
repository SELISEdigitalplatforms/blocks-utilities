using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Giving out and taking back the seats a subscription was bought with.
/// </summary>
public interface ISubscriptionMemberService
{
    /// <summary>
    /// Puts one person on a seat.
    /// </summary>
    /// <remarks>
    /// Refused when the subscription is not user-wise, when it grants nothing any more, or when
    /// every seat it paid for is already held.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionMemberResponse>> AssignAsync(
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
