using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Services;

public interface ISubscriptionAuditTrail
{
    Task RecordAsync(SubscriptionAuditEvent auditEvent, CancellationToken cancellationToken);
}
