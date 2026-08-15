using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public interface ISubscriptionUsageInvoiceRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts an invoice, returning false when one already exists for this subscription and
    /// period — the double-billing guard, enforced by the database rather than a read-then-write.
    /// </summary>
    Task<bool> TryCreateAsync(
        SubscriptionUsageInvoice invoice,
        CancellationToken cancellationToken);

    Task<SubscriptionUsageInvoice?> GetAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken);

    /// <summary>Pending invoices the charge sweep should look at now.</summary>
    Task<IReadOnlyList<SubscriptionUsageInvoice>> ListDueAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TryMarkChargedAsync(
        string tenantId,
        string invoiceId,
        string paymentDetailId,
        CancellationToken cancellationToken);

    Task<bool> TryMarkNoChargeAsync(
        string tenantId,
        string invoiceId,
        CancellationToken cancellationToken);

    Task<bool> TryMarkAbandonedAsync(
        string tenantId,
        string invoiceId,
        CancellationToken cancellationToken);

    Task RescheduleAsync(
        string tenantId,
        string invoiceId,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string? failureReason,
        CancellationToken cancellationToken);
}
