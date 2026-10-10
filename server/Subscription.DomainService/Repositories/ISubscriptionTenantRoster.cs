namespace Subscription.DomainService.Repositories;

/// <summary>
/// Puts a tenant on the roster the subscription background sweeps visit.
/// </summary>
/// <remarks>
/// Called before a subscription is first persisted, so no subscription can exist for a tenant the
/// sweeps do not know about. See <see cref="SubscriptionTenantRoster"/>.
/// </remarks>
public interface ISubscriptionTenantRoster
{
    Task RecordAsync(string tenantId, CancellationToken cancellationToken);
}
