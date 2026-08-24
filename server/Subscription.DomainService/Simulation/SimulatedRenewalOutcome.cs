namespace Subscription.DomainService.Simulation;

/// <summary>
/// What the one gateway call an advanced renewal makes should report — success folded in
/// alongside the same failure vocabulary <see cref="SimulatedPaymentFailureKind"/> uses, since
/// this single request field has to name either.
/// </summary>
public enum SimulatedRenewalOutcome
{
    Succeeded,
    Declined,
    InsufficientFunds,
    PaymentMethodExpired,
    ProviderUnavailable,
    OutcomeUnknown
}
