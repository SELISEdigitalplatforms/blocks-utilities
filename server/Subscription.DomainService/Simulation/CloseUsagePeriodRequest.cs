namespace Subscription.DomainService.Simulation;

public sealed class CloseUsagePeriodRequest
{
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The gateway outcome to script for the overage charge, if <see cref="ChargeInvoice"/> is
    /// true and the closed period actually produced one. Reuses the same vocabulary
    /// <see cref="AdvanceRenewalRequest.PaymentOutcome"/> does — a usage invoice is charged
    /// through the identical <c>ISubscriptionBillingGateway.ChargeAsync</c> call a renewal is.
    /// Omitted (or left null), the charge is instead sent to the real payment gateway.
    /// </summary>
    public SimulatedRenewalOutcome? PaymentOutcome { get; set; }

    /// <summary>Whether to also charge the overage invoice the close produces, if any.</summary>
    public bool ChargeInvoice { get; set; } = true;

    /// <summary>Must be true in this version — see <see cref="AdvanceRenewalRequest.RunImmediately"/>.</summary>
    public bool RunImmediately { get; set; } = true;
}
