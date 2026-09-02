using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The published current-usage projection.
/// </summary>
/// <remarks>
/// Read-optimised and derived. Nothing here computes a balance: every write takes the figures a
/// counter result already produced. See <see cref="SubscriptionUsageCurrent"/> for why this is never
/// an authority.
/// </remarks>
public interface ISubscriptionUsageCurrentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one projected document, unless a newer one is already stored.
    /// </summary>
    /// <remarks>
    /// The whole concurrency story of this feature is in the filter. Two recordings against the same
    /// meter finish in an order nobody controls, so the write is conditional on
    /// <see cref="SubscriptionUsageCurrent.CounterVersion"/> being newer than what is stored: the
    /// highest version wins rather than the last writer. Without that, a request delayed between its
    /// counter update and its projection write would overwrite a newer balance with an older one and
    /// leave the projection permanently behind with nothing to detect it.
    /// <para>
    /// Returns false when the stored document was already at or beyond this version. That is a
    /// success, not a failure: the state this call wanted published is published, by someone else.
    /// </para>
    /// </remarks>
    Task<bool> TryPublishAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a zero-usage document if this meter-period has none, and leaves any existing one
    /// exactly as it is.
    /// </summary>
    /// <remarks>
    /// So a consumer can discover a meter and its allowance before anything has been recorded against
    /// it, which is the difference between "no usage" and "no such meter" for a reader that cannot see
    /// the plan. Insert-only: it must never reset a balance, so it cannot be expressed as the publish
    /// above with a zero version — a plain upsert would race a real recording and lose usage.
    /// </remarks>
    Task<bool> TrySeedAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken);

    /// <summary>
    /// The current windows for one subscription: the documents whose period contains
    /// <paramref name="asOfUtc"/>.
    /// </summary>
    /// <remarks>
    /// Organization-scoped in the filter, not merely in the caller's intent. A projection read that
    /// omitted it would be a cross-organization read of billing state.
    /// </remarks>
    Task<IReadOnlyList<SubscriptionUsageCurrent>> ListCurrentAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        DateTime asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Current-window projections for a tenant, oldest first, for the reconciliation pass to compare
    /// against their counters.
    /// </summary>
    /// <remarks>
    /// Oldest-updated first, because a projection that has not been written for a while is the one
    /// most likely to be behind: a busy meter republishes on every recording. Bounded by
    /// <paramref name="limit"/> so one pass costs the same whatever the tenant's size.
    /// <para>
    /// This returns <em>candidates</em>, not documents known to be stale. Whether one is behind can
    /// only be settled by reading its counter, which the caller does in a batch.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SubscriptionUsageCurrent>> ListBehindCountersAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<SubscriptionUsageCurrent?> GetAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken);
}
