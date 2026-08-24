using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Simulation;

/// <summary>
/// The allowlisted, read-mostly Mongo access this harness offers once the purpose-built actions
/// in PRs 2-5 cannot reach what a test needs. Never a raw query: every operation is scoped to
/// one collection from <see cref="SubscriptionSimulationDataConsolePolicy"/>, one tenant, one
/// organization and one subscription.
/// </summary>
public interface ISubscriptionSimulationDataConsoleService
{
    Task<SubscriptionOperationResult<SubscriptionSimulationDataQueryResponse>> FindAsync(
        string logicalCollection,
        FindDataRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>> UpdateFieldsAsync(
        string logicalCollection,
        UpdateDataFieldRequest request,
        string correlationId,
        CancellationToken cancellationToken);
}
