using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface IEntitlementSnapshotCache
{
    /// <summary>
    /// The subscriptions granting something to one subscriber, cached briefly.
    /// </summary>
    /// <remarks>
    /// Keyed on the subscriber, not just the organization. Two people in one organization can hold
    /// different user-wise plans, and a cache that could not tell them apart would answer one
    /// person's allowance with another's.
    /// </remarks>
    Task<IReadOnlyList<SubscriptionDetail>> GetAsync(
        string tenantId,
        string organizationId,
        string subscriberUserId,
        Func<Task<IReadOnlyList<SubscriptionDetail>>> loader);

    /// <summary>
    /// Drops everything cached for an organization, every subscriber included.
    /// </summary>
    /// <remarks>
    /// Organization-wide rather than per subscriber because every subscriber's entry carries the
    /// organization's own subscription alongside their own. A change to it staleness every one of
    /// them, and the caller that reports the change — an outbox processor holding one
    /// <see cref="SubscriptionDetail"/> — has no way to enumerate the users who cached it.
    /// <para>
    /// This clears more than strictly necessary when what changed was one person's own plan. That
    /// is the cheaper mistake: entries live for seconds, and the alternative is serving an
    /// allowance the subscriber no longer has.
    /// </para>
    /// </remarks>
    void Invalidate(string tenantId, string organizationId);
}
