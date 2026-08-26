namespace Subscription.DomainService.Simulation;

/// <summary>
/// Runs due background work for exactly one subscription — never a tenant-wide sweep. The
/// subscription is always the URL's own <c>{subscriptionId}</c>, which is what keeps this from
/// becoming an unrestricted "run every job for every tenant" action.
/// </summary>
public sealed class RunDueJobsRequest
{
    public string? OrganizationId { get; set; }

    /// <summary>Empty means every work type this endpoint knows about.</summary>
    public List<SimulationWorkType> WorkTypes { get; set; } = [];
}
