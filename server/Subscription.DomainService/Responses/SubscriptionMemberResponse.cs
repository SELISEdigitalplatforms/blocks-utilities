namespace Subscription.DomainService.Responses;

/// <summary>One seat, as a caller sees it.</summary>
public sealed class SubscriptionMemberResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public DateTime AssignedAtUtc { get; init; }

    public DateTime? ReleasedAtUtc { get; init; }
}

/// <summary>
/// Who holds this subscription's seats, and how many remain.
/// </summary>
/// <remarks>
/// <see cref="Available"/> is what it was a moment ago, not a promise about the next assignment:
/// only the unique index settles a race for the last seat. It is here so an administrator can be
/// shown how full a subscription is, not so a caller can decide whether an assignment will succeed.
/// </remarks>
public sealed class SubscriptionMembersResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>How many seats the subscription was bought with.</summary>
    public long Purchased { get; init; }

    public long Held { get; init; }

    public long Available { get; init; }

    public List<SubscriptionMemberResponse> Seats { get; init; } = [];
}
