namespace Subscription.DomainService.Responses;

/// <summary>One seat, as a caller sees it.</summary>
public sealed class SubscriptionMemberResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Which place, 1-based. Null on a release, which answers who left rather than where from.
    /// </summary>
    public int? SeatNumber { get; init; }

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

    /// <summary>How many can be filled today: the smaller of bought and scheduled, less those held.</summary>
    public long Available { get; init; }

    /// <summary>
    /// How many places a scheduled decrease leaves, or null when none is scheduled. Places above
    /// it can no longer be filled; they go when the period turns over.
    /// </summary>
    public long? ScheduledPlaces { get; init; }

    /// <summary>When <see cref="ScheduledPlaces"/> takes effect.</summary>
    public DateTime? ScheduledAtUtc { get; init; }

    public List<SubscriptionMemberResponse> Seats { get; init; } = [];

    /// <summary>
    /// Each place's usage in its current windows, one entry per place and meter, read from the
    /// usage projection. A place used and since released is still here, with no holder.
    /// </summary>
    public List<PlaceUsageResponse> Usage { get; init; } = [];
}

/// <summary>A place the caller holds, and the subscription it is on.</summary>
public sealed class HeldPlaceResponse
{
    public string SubscriptionId { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>Which place, 1-based — the one whose allowance this person spends.</summary>
    public int SeatNumber { get; init; }

    public DateTime CurrentPeriodEndUtc { get; init; }

    /// <summary>True when a cancellation is scheduled for the end of the current period.</summary>
    public bool CancelAtPeriodEnd { get; init; }
}

/// <summary>One place's usage of one meter in its current window.</summary>
public sealed class PlaceUsageResponse
{
    public int SeatNumber { get; init; }

    /// <summary>Who holds the place now; empty when nobody does.</summary>
    public string UserId { get; init; } = string.Empty;

    public string MeterKey { get; init; } = string.Empty;

    public string UnitLabel { get; init; } = string.Empty;

    public int QuantityScale { get; init; }

    public decimal Included { get; init; }

    /// <summary>The place's, so it includes whatever an earlier holder spent in this window.</summary>
    public decimal Used { get; init; }

    public decimal Remaining { get; init; }

    public decimal Overage { get; init; }

    public DateTime PeriodEndUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>Each pace as the last recording left it; empty until one has.</summary>
    public List<PlaceSubLimitResponse> SubLimits { get; init; } = [];
}

public sealed class PlaceSubLimitResponse
{
    public string Window { get; init; } = string.Empty;

    public int WindowCount { get; init; } = 1;

    public bool Rolling { get; init; }

    public string Behaviour { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal Used { get; init; }

    public decimal Remaining { get; init; }

    public bool Exceeded { get; init; }

    public DateTime WindowStartUtc { get; init; }

    /// <summary>When a fixed window resets; null for a rolling one.</summary>
    public DateTime? WindowEndUtc { get; init; }
}
