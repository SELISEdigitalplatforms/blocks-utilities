namespace Subscription.DomainService.Services;

/// <summary>
/// Refuses a money-moving operation when there is nobody to address its invoice to.
/// </summary>
/// <remarks>
/// One collaborator rather than the same read repeated in three services, because the rule has an
/// edge and three copies of an edge is three chances to get it differently: it applies to operations
/// that move money and not to free ones, it is switchable for an installation mid-migration, and a
/// missing profile is a validation failure with a field list rather than a message.
/// <para>
/// Checked before the money moves, which is the only point at which refusing costs nothing. Once a
/// charge has settled the invoice is owed whatever the profile says, and the document falls back to
/// naming the subscriber by their organization id.
/// </para>
/// </remarks>
public interface ISubscriptionBillingProfileGuard
{
    /// <returns>
    /// The fields still missing, or empty when the operation may proceed — including when the
    /// requirement is switched off, which is what makes this safe to call unconditionally.
    /// </returns>
    Task<IReadOnlyList<string>> MissingFieldsAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the acting user as somebody a document may have to name as initiator.
    /// </summary>
    /// <remarks>
    /// Separate from the check, and best-effort: failing to remember a name must never fail an
    /// operation the subscriber asked for. Called after the check passes, so it never writes a
    /// contact for an operation that was then refused.
    /// </remarks>
    /// <summary>
    /// Records who acted, under their own name, so a document can name them rather than an identifier.
    /// </summary>
    /// <param name="name">
    /// What the caller's identity provider calls them. Their own name, not the organization's billing
    /// contact: those two are the same person only by coincidence, and printing the second where the
    /// first was meant produces a document naming somebody who did nothing.
    /// </param>
    /// <param name="email">The caller's own address, for the same reason.</param>
    Task RememberInitiatorAsync(
        string tenantId,
        string organizationId,
        string? userId,
        string? name,
        string? email,
        CancellationToken cancellationToken);
}
