namespace Subscription.DomainService.Simulation;

public sealed class AdvanceRenewalRequest
{
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Must be 1 in this version. There is no simulated clock: this advances the one renewal
    /// <see cref="Subscription.DomainService.Services.SubscriptionRenewalService.RenewAsync"/>
    /// would raise for the fee schedule's current period, not a run of several future periods.
    /// </summary>
    public int Periods { get; set; } = 1;

    /// <summary>
    /// The gateway outcome to script for this renewal's charge. Omitted (or left null), the
    /// charge is instead sent to the real payment gateway — the same call a production renewal
    /// makes — which is how this advances a renewal against a real test-mode provider account
    /// instead of a scripted result.
    /// </summary>
    public SimulatedRenewalOutcome? PaymentOutcome { get; set; }

    /// <summary>
    /// Must be true in this version — there is nothing to schedule against without a simulated
    /// clock, only an immediate attempt.
    /// </summary>
    public bool RunImmediately { get; set; } = true;
}
