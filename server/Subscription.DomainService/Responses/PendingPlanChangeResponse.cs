namespace Subscription.DomainService.Responses;

/// <summary>
/// A plan change already booked for the end of the period the subscriber has paid for.
/// </summary>
/// <remarks>
/// Describes the target, not the plan in force. Everything else on the subscription — the plan,
/// price, quantities and renewal amount — still describes what is being paid for today, because
/// that is what the subscriber still has until <see cref="EffectiveAtUtc"/>.
/// </remarks>
public sealed class PendingPlanChangeResponse
{
    public string TargetPlanCode { get; init; } = string.Empty;

    public string TargetPlanName { get; init; } = string.Empty;

    public string TargetPriceId { get; init; } = string.Empty;

    /// <summary>The target's own cadence, which a plan change may move.</summary>
    public string Interval { get; init; } = string.Empty;

    public int IntervalCount { get; init; }

    /// <summary>The quantities that come into force with the target plan.</summary>
    public List<SubscriptionQuantityResponse> Quantities { get; init; } = [];

    public DateTime RequestedAtUtc { get; init; }

    /// <summary>When this becomes the plan in force.</summary>
    public DateTime EffectiveAtUtc { get; init; }
}
