namespace Subscription.DomainService.Simulation;

/// <summary>What a scripted charge should report, in the gateway's own vocabulary.</summary>
public enum SimulatedChargeOutcome
{
    Succeeded,
    Rejected,
    Unavailable,

    /// <summary>The provider may have already taken the money; nothing here says either way.</summary>
    TimedOut
}

public sealed record ScriptedChargeOutcome(
    SimulatedChargeOutcome Outcome,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Holds one scripted charge outcome, consumed exactly once.
/// </summary>
/// <remarks>
/// Scoped per request rather than shared across the tenant: it is set immediately before the
/// one gateway call a simulated renewal makes and consumed by that call alone, so it can never
/// leak into a renewal a different request triggers. See
/// <see cref="SubscriptionSimulationBillingGateway"/> for the consumer.
/// </remarks>
public interface ISubscriptionSimulatedOutcomeSource
{
    void ScriptNext(ScriptedChargeOutcome outcome);

    bool TryConsume(out ScriptedChargeOutcome outcome);
}

public sealed class SubscriptionSimulatedOutcomeSource : ISubscriptionSimulatedOutcomeSource
{
    private ScriptedChargeOutcome? _next;

    public void ScriptNext(ScriptedChargeOutcome outcome) => _next = outcome;

    public bool TryConsume(out ScriptedChargeOutcome outcome)
    {
        if (_next is { } value)
        {
            outcome = value;
            _next = null;

            return true;
        }

        outcome = null!;

        return false;
    }
}
