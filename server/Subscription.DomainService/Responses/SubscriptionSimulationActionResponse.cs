namespace Subscription.DomainService.Responses;

/// <summary>
/// What a simulation mutation did, alongside the complete state it left behind — the shape every
/// simulation action (not just the read-only inspector) returns.
/// </summary>
public sealed class SubscriptionSimulationActionResponse
{
    public string SimulationRunId { get; init; } = string.Empty;

    /// <summary>E.g. <c>MarkPaymentSucceeded</c>, <c>MarkPaymentFailed</c>.</summary>
    public string Action { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }

    public DateTime CompletedAtUtc { get; init; }

    public SubscriptionSimulationSummary Before { get; init; } = new();

    public SubscriptionSimulationSummary After { get; init; } = new();

    public SubscriptionSimulationStateResponse State { get; init; } = new();

    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// The handful of fields worth comparing before and after an action, without repeating the
/// entire state twice.
/// </summary>
public sealed class SubscriptionSimulationSummary
{
    public string SubscriptionStatus { get; init; } = string.Empty;

    public DateTime? CurrentPeriodEndUtc { get; init; }

    public DateTime? NextFeeBillingAtUtc { get; init; }

    public int DunningAttemptCount { get; init; }

    public string? LastRenewalPaymentDetailId { get; init; }

    public int Version { get; init; }
}
