using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Outbox;

/// <summary>
/// Publishes the events an organization's credit grants carry (spec 005). The grant document is
/// the organization's outbox, the way a subscription document is its own.
/// </summary>
public interface IOrganizationCreditOutboxProcessor
{
    /// <summary>Every due event on every organization grant in the tenant, as the sweep runs it.</summary>
    Task<int> PublishDueAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The due events on one grant, run right after the write that added them so a consumer hears
    /// within milliseconds rather than at the next sweep. Anything that fails here is left for the
    /// sweep, which is what makes the two together at-least-once.
    /// </summary>
    Task<int> PublishDueForGrantAsync(SubscriptionCreditGrant grant, CancellationToken cancellationToken);
}
