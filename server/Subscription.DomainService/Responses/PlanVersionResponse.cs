namespace Subscription.DomainService.Responses;

/// <summary>
/// One superseded version of a plan: what it sold before a later write replaced it.
/// </summary>
/// <remarks>
/// <see cref="Plan"/> carries no prices. Prices are separate documents with their own versions,
/// sold and retired on their own, and are not copied when the plan's terms change — what a given
/// subscriber pays is on their own price snapshot, not here.
/// </remarks>
public sealed class PlanVersionResponse
{
    public int Version { get; init; }

    public DateTime SupersededAtUtc { get; init; }

    public PlanResponse Plan { get; init; } = null!;
}
