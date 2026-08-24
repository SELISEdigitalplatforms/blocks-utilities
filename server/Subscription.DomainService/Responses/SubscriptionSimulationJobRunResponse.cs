namespace Subscription.DomainService.Responses;

/// <summary>
/// What running due background work for one subscription actually did — each work type's own
/// outcome, alongside the complete state left behind.
/// </summary>
public sealed class SubscriptionSimulationJobRunResponse
{
    public string SimulationRunId { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime CompletedAtUtc { get; init; }

    public int Claimed { get; init; }

    public int Completed { get; init; }

    /// <summary>Work types that were asked for but were not actually due — not a failure.</summary>
    public int NotDue { get; init; }

    public List<SubscriptionSimulationJobResultResponse> Jobs { get; init; } = [];

    public SubscriptionSimulationStateResponse State { get; init; } = new();

    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class SubscriptionSimulationJobResultResponse
{
    public string WorkType { get; init; } = string.Empty;

    /// <summary><c>Completed</c>, <c>NotDue</c> or <c>NotApplicable</c> (the subscription's own state rules it out entirely).</summary>
    public string Status { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public long DurationMs { get; init; }
}
