namespace Subscription.DomainService.Simulation;

/// <summary>
/// A read from one allowlisted collection, scoped to a tenant, an organization and — always —
/// one subscription. There is no field for a raw filter or query object: the only scoping this
/// endpoint accepts is these three identifiers, which is what keeps it from becoming an
/// unrestricted Mongo query surface.
/// </summary>
public sealed class FindDataRequest
{
    public string? OrganizationId { get; set; }

    public string SubscriptionId { get; set; } = string.Empty;

    public int Limit { get; set; } = 20;
}
