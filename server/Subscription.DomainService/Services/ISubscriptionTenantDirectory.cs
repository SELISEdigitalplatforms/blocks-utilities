namespace Subscription.DomainService.Services;

/// <summary>
/// Which tenants the background sweeps run for.
/// </summary>
/// <remarks>
/// Asked repeatedly rather than read once. Projects are created at any time and can subscribe
/// immediately, so a roster captured at startup is stale the moment the next one appears — and a
/// tenant the sweep never visits is a tenant whose renewals silently never happen.
/// </remarks>
public interface ISubscriptionTenantDirectory
{
    Task<IReadOnlyList<string>> ListTenantIdsAsync(CancellationToken cancellationToken);
}
