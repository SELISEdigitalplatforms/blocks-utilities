namespace Subscription.DomainService.Requests;

/// <summary>
/// Who to put on a subscription.
/// </summary>
/// <remarks>
/// A list, because an administrator filling a ten-person subscription should not make ten calls
/// and reconcile ten answers. One name is a list of one.
/// <para>
/// People are named in the body rather than taken from the caller's token: filling places on
/// behalf of others is the ordinary case. Which organization's subscription may be touched is
/// still decided by the token.
/// </para>
/// </remarks>
public sealed class AssignMemberRequest
{
    public List<string> UserIds { get; set; } = [];
}
