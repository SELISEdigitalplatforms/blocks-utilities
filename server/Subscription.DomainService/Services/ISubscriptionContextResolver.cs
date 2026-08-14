namespace Subscription.DomainService.Services;

/// <summary>
/// Who is asking, and on whose behalf.
/// </summary>
public interface ISubscriptionContextResolver
{
    /// <summary>
    /// Resolves the caller's tenant, organization and actor.
    /// </summary>
    /// <remarks>
    /// Unlike the payment resolver this wraps, a blank organization is refused rather than
    /// treated as "sees everything in the tenant". Entitlement without an organization has no
    /// meaning, and a machine-to-machine caller with no organization would otherwise receive an
    /// unscoped answer — which is the shape of an access-control failure, not a convenience.
    /// </remarks>
    SubscriptionContextResolution Resolve(string correlationId);
}
