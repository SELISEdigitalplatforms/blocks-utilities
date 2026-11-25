using Payment.DomainService.Entities;

namespace Payment.DomainService.Repositories;

public interface IPaymentAuditRepository
{
    Task AppendAsync(PaymentAuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Newest first. <paramref name="organizationId"/> null means every organization in the tenant
    /// (the console); the subject filters are optional.
    /// </summary>
    Task<IReadOnlyList<PaymentAuditEvent>> ListAsync(
        string tenantId,
        string? organizationId,
        string? subjectType,
        string? subjectId,
        int limit,
        CancellationToken cancellationToken);
}
