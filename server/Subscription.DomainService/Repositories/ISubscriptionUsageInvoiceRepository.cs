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

    /// <summary>Every usage invoice raised for one subscription, newest period first.</summary>
    Task<IReadOnlyList<SubscriptionUsageInvoice>> ListBySubscriptionAsync(
        string tenantId,
        string subscriptionId,
        int limit,
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

    /// <summary>
    /// Closed usage periods across the tenant, optionally narrowed to one organization or
    /// subscription and a creation-date window — the closed-period half of the allowance-history
    /// report. Newest first, keyset-paged the same way
    /// <see cref="ISubscriptionFinancialDocumentRepository.ListAsync"/> is.
    /// </summary>
    Task<UsageInvoicePage> ListAsync(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageInvoiceCursor? after,
        CancellationToken cancellationToken);
}

public sealed record UsageInvoicePage(
    IReadOnlyList<SubscriptionUsageInvoice> Items,
    bool HasMore);

public sealed record UsageInvoiceCursor(DateTime CreatedAtUtc, string InvoiceId);
