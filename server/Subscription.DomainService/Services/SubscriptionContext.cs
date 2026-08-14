namespace Subscription.DomainService.Services;

/// <summary>
/// The caller, resolved. Every value comes from the authenticated context, never the request.
/// </summary>
/// <remarks>
/// Taking the organization from the request instead would let anyone read or change another
/// organization's subscription by naming it, which no amount of filtering downstream can undo.
/// </remarks>
public sealed record SubscriptionContext(
    string TenantId,
    string OrganizationId,
    string ActorId,
    string? UserId);
