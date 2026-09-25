namespace Subscription.DomainService.Requests;

/// <summary>
/// Who to put on a seat.
/// </summary>
/// <remarks>
/// The user is named in the body rather than taken from the caller's token, because the ordinary
/// case is an administrator filling seats on behalf of other people. Which organization's
/// subscription may be touched is still decided by the token, not by this.
/// </remarks>
public sealed class AssignMemberRequest
{
    public string UserId { get; set; } = string.Empty;
}
