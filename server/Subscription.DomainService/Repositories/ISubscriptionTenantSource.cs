namespace Subscription.DomainService.Repositories;

/// <summary>
/// Where the list of tenants comes from.
/// </summary>
/// <remarks>
/// Its own type so the policy around the list — caching it, holding the last good one when a
/// read fails — can be tested without a database. This half is the part that cannot be.
/// </remarks>
public interface ISubscriptionTenantSource
{
    Task<IReadOnlyList<string>> ListTenantIdsAsync(CancellationToken cancellationToken);
}
