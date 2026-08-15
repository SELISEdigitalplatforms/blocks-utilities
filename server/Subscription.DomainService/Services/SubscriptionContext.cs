namespace Subscription.DomainService.Services;

/// <summary>
/// The caller, resolved.
/// </summary>
/// <remarks>
/// Every value comes from the authenticated context — except <see cref="OrganizationId"/>,
/// which the platform console alone may override by naming one in the request. Everyone else's
/// organization is exactly the one their token carries, no amount of filtering downstream can
/// undo a wider rule than that. See <see cref="ISubscriptionContextResolver.ResolveAsync"/>.
/// </remarks>
public sealed record SubscriptionContext(
    string TenantId,
    string OrganizationId,
    string ActorId,
    string? UserId);
