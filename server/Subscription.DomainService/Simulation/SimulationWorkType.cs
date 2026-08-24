namespace Subscription.DomainService.Simulation;

/// <summary>
/// The background sweeps this harness can run for one subscription immediately, instead of
/// waiting for the real reconciliation host's own polling interval.
/// </summary>
public enum SimulationWorkType
{
    Renewal,
    UsagePeriodClosure,
    UsageInvoiceCharge,
    OutboxPublication
}
