namespace Subscription.DomainService.Services;

/// <summary>
/// Who a member is, for the email telling them they gained or lost a seat.
/// </summary>
/// <remarks>
/// Seats are recorded by user id alone, and the mail needs an address. Asked while the
/// administrator's request is still in hand, because the lookup runs on their token — the worker
/// that later sends the mail has no caller, and holds no credential of its own to ask with.
/// <para>
/// Never throws and never refuses: a person IAM cannot describe is still seated, just not mailed.
/// </para>
/// </remarks>
public interface IMemberDirectory
{
    /// <summary>The person, or null when IAM could not or would not say.</summary>
    Task<MemberContact?> FindUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The organization's display name, or null when IAM could not or would not say.</summary>
    Task<string?> FindOrganizationNameAsync(string organizationId, CancellationToken cancellationToken);
}

/// <summary>What the member emails need to know about a person.</summary>
/// <param name="Email">Never empty: a person with no address is reported as not found.</param>
/// <param name="DisplayName">First and last name, or the address when IAM holds neither.</param>
/// <param name="Language">The person's language in IAM, or null when unset.</param>
public sealed record MemberContact(string Email, string DisplayName, string? Language);
