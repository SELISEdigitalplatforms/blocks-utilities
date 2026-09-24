using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Who holds which seat.
/// </summary>
public interface ISubscriptionAssignmentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives a seat to one person.
    /// </summary>
    /// <remarks>
    /// The seat count is not checked here, and cannot be: "are there seats left" is a read, and a
    /// read followed by a write lets two administrators past the same last seat. The caller
    /// reserves against the purchased quantity and this records the result — see
    /// <see cref="CountActiveAsync"/> for the figure that reservation is made against.
    /// </remarks>
    Task<SeatAssignmentOutcome> TryAssignAsync(
        SubscriptionAssignment assignment,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands a seat back, leaving the record behind.
    /// </summary>
    /// <remarks>
    /// Released rather than deleted, because a period's usage outlives the person who spent it.
    /// The meter counts against the subscription and is not reset when a seat changes hands, so
    /// this is the only record of why the next holder inherited a part-spent window.
    /// </remarks>
    Task<SeatReleaseOutcome> TryReleaseAsync(
        string tenantId,
        string subscriptionId,
        string userId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// The subscriptions this person currently holds a seat on, within one organization.
    /// </summary>
    /// <remarks>
    /// What entitlement asks on every gated action. Returns identifiers rather than subscriptions:
    /// the caller already reads subscriptions by id and would otherwise pay for the same documents
    /// twice.
    /// </remarks>
    Task<IReadOnlyList<string>> ListSubscriptionIdsForUserAsync(
        string tenantId,
        string organizationId,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>Everyone currently holding a seat on one subscription.</summary>
    Task<IReadOnlyList<SubscriptionAssignment>> ListActiveAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many seats on this subscription are currently held.
    /// </summary>
    /// <remarks>
    /// Advisory, exactly like any other count read before a write: it says how full the
    /// subscription was a moment ago, not whether the next assignment will fit. Only the unique
    /// index settles a race for the last seat.
    /// </remarks>
    Task<long> CountActiveAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases every seat on a subscription at once, for when the subscription itself ends.
    /// </summary>
    /// <returns>How many seats were still held.</returns>
    Task<long> ReleaseAllAsync(
        string tenantId,
        string subscriptionId,
        DateTime releasedAtUtc,
        CancellationToken cancellationToken);
}
