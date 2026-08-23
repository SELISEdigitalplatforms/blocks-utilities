using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionSimulationRunRepository
{
    Task AppendAsync(SubscriptionSimulationRun run, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSimulationRun>> ListAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken);
}
