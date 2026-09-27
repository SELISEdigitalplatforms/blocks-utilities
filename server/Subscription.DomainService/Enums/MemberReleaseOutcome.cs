namespace Subscription.DomainService.Enums;

/// <summary>
/// What became of an attempt to hand a seat back.
/// </summary>
/// <remarks>
/// <see cref="NotHeld"/> covers both "this person never had a seat here" and "it was already given
/// up", deliberately as one value. The caller does nothing different between them, and telling them
/// apart would mean reading the released rows back to find out which — a query that exists only to
/// refine an answer nobody acts on.
/// </remarks>
public enum MemberReleaseOutcome
{
    Released = 0,

    /// <summary>There was no live seat to give back.</summary>
    NotHeld = 1
}
