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

    /// <summary>
    /// One catalogue aggregate's history, newest first — a plan's, say, including its prices'.
    /// </summary>
    /// <remarks>
    /// Tenant-scoped rather than organization-scoped: a tenant-wide plan is edited from the console,
    /// whose events carry the console's organization. The caller checks the aggregate is visible.
    /// </remarks>
    /// <param name="legacyOperationIdPrefix">
    /// Also matches events written before the aggregate fields were set, by the start of their
    /// <c>OperationId</c> — discount events used to carry only <c>discount:{id}:…</c>.
    /// </param>
    Task<IReadOnlyList<SubscriptionAuditEvent>> ListForAggregateAsync(
        string tenantId,
        string aggregateType,
        string aggregateId,
        int limit,
        CancellationToken cancellationToken,
        string? legacyOperationIdPrefix = null);
}
