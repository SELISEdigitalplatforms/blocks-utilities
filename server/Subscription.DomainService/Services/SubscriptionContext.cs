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
/// <param name="UserName">
/// What the identity provider calls the caller, for records that have to name the person who acted.
/// </param>
/// <param name="UserEmail">
/// The caller's own address. Deliberately separate from the organization's billing contact: they are
/// the same person only by coincidence, and a document that prints one where it means the other names
/// somebody who did nothing.
/// </param>
public sealed record SubscriptionContext(
    string TenantId,
    string OrganizationId,
    string ActorId,
    string? UserId,
    string? UserName = null,
    string? UserEmail = null)
{
    /// <summary>
    /// How a notification email names the caller: their name, else their address, else nobody.
    /// </summary>
    /// <remarks>
    /// Stored on a scheduled change at the moment it is asked for, because the worker that carries
    /// it out weeks later has no caller of its own to ask.
    /// </remarks>
    public string? ActorName => ActorNameOf(UserName, UserEmail);

    /// <summary>The rule behind <see cref="ActorName"/>, for callers holding the two values apart.</summary>
    public static string? ActorNameOf(string? userName, string? userEmail) =>
        !string.IsNullOrWhiteSpace(userName) ? userName.Trim()
        : !string.IsNullOrWhiteSpace(userEmail) ? userEmail.Trim()
        : null;
}
