using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionBillingProfileRepository
{
    Task<SubscriptionBillingProfile?> GetAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the organization's profile, creating it if there is none.
    /// </summary>
    /// <returns>The profile as stored, so a caller can return exactly what was persisted.</returns>
    Task<SubscriptionBillingProfile> UpsertAsync(
        SubscriptionBillingProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records one person's name and address against the organization, without touching the rest.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UpsertAsync"/> because it is called from the money path rather than
    /// from an authoring request: whoever starts a subscription is, by acting, the person a document
    /// has to name. Folding it into the upsert would mean a subscribe request could rewrite the
    /// organization's legal name as a side effect.
    /// </remarks>
    Task RecordContactAsync(
        string tenantId,
        string organizationId,
        BillingContact contact,
        CancellationToken cancellationToken);
}
