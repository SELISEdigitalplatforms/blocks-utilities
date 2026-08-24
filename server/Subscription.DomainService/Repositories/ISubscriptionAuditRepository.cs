using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionAuditRepository
{
    Task AppendAsync(SubscriptionAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionAuditEvent>> ListAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken);
}
