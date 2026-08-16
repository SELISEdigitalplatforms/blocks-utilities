using Payment.DomainService.Services;

namespace Subscription.DomainService.Services;

/// <summary>
/// Who is asking, and on whose behalf.
/// </summary>
public interface ISubscriptionContextResolver
{
    /// <summary>
    /// Resolves the caller's tenant, organization and actor.
    /// </summary>
    /// <param name="requestedOrganizationId">
    /// An organization named by the request, if any. Trusted only for the platform console —
    /// see <see cref="IPaymentOrganizationResolver"/>, the same policy payment writes and reads
    /// already use. Every other caller's own token organization wins regardless of what this
    /// carries.
    /// </param>
    /// <remarks>
    /// Unlike the payment resolver this wraps, a blank <em>resolved</em> organization is refused
    /// rather than treated as "sees everything in the tenant". Entitlement without an
    /// organization has no meaning, and a machine-to-machine caller with no organization would
    /// otherwise receive an unscoped answer — which is the shape of an access-control failure,
    /// not a convenience.
    /// </remarks>
    Task<SubscriptionContextResolution> ResolveAsync(
        string correlationId,
        string? requestedOrganizationId,
        CancellationToken cancellationToken);
}
