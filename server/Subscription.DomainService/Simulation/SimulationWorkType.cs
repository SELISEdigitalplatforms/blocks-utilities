using System.Text.Json.Serialization;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// The background sweeps this harness can run for one subscription immediately, instead of
/// waiting for the real reconciliation host's own polling interval.
/// </summary>
/// <remarks>
/// Explicit string conversion — see the remark on <see cref="SimulatedRenewalOutcome"/> for why
/// a request-bound enum needs this even though this API's response enums do not.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SimulationWorkType
{
    Renewal,
    UsagePeriodClosure,
    UsageInvoiceCharge,
    OutboxPublication
}
