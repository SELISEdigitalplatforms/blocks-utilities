using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Simulation;

public interface ISubscriptionSimulationService
{
    /// <summary>
    /// A complete, read-only snapshot of one subscription — plan and price, entitlements,
    /// settlement reservation, pending checkout, settled payments, usage invoices, background
    /// work and recent audit events — for the simulation harness.
    /// </summary>
    /// <param name="organizationId">
    /// Required. Simulation is console-only, and the console has no subscription of its own to
    /// inspect — the caller always names whose subscription it means.
    /// </param>
    Task<SubscriptionOperationResult<SubscriptionSimulationStateResponse>> GetStateAsync(
        string subscriptionId,
        string? organizationId,
        int auditLimit,
        int paymentLimit,
        bool includeBackgroundWork,
        string correlationId,
        CancellationToken cancellationToken);
}
