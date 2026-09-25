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
/// What became of each name in an assignment request.
/// </summary>
/// <remarks>
/// Reported per person rather than as one verdict for the batch. Nothing here spans documents, so
/// a batch cannot be atomic, and saying it had failed when seven of ten landed would send an
/// administrator looking for seven assignments that are already there.
/// </remarks>
public sealed class SubscriptionMemberAssignmentResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>The people now holding a place, whether this call put them there or they already did.</summary>
    public List<SubscriptionMemberResponse> Assigned { get; init; } = [];

    /// <summary>Who could not be assigned, and why, in the order they were named.</summary>
    public List<SubscriptionMemberRefusalResponse> Refused { get; init; } = [];
}

public sealed class SubscriptionMemberRefusalResponse
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>The same code a single assignment would have failed with.</summary>
    public string ReasonCode { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Who holds this subscription's places, and how many remain.
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
