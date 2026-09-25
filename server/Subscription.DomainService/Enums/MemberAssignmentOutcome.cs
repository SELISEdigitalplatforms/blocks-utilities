namespace Subscription.DomainService.Enums;

/// <summary>
/// What became of an attempt to give someone a seat.
/// </summary>
/// <remarks>
/// <see cref="AlreadyHeld"/> is separated from <see cref="Assigned"/> rather than folded into it,
/// even though both leave the person holding the seat. An administrator who assigns someone twice
/// should be told the second one changed nothing, and a caller that counts seats off successful
/// assignments would otherwise consume two.
/// </remarks>
public enum MemberAssignmentOutcome
{
    Assigned = 0,

    /// <summary>This person already holds a live seat on this subscription.</summary>
    AlreadyHeld = 1
}
