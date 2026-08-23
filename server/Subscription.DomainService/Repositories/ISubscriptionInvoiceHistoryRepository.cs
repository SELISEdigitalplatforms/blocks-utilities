namespace Subscription.DomainService.Repositories;

public interface ISubscriptionInvoiceHistoryRepository
{
    Task<SubscriptionInvoiceHistoryPage> ListAsync(
        string tenantId,
        string organizationId,
        int pageSize,
        SubscriptionInvoiceHistoryCursor? after,
        CancellationToken cancellationToken);

    /// <summary>
    /// The same settled, invoiced payments as <see cref="ListAsync"/>, narrowed to one
    /// subscription rather than paged across the whole organization.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInvoiceHistoryRecord>> ListBySubscriptionAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record SubscriptionInvoiceHistoryCursor(
    DateTime IssuedAtUtc,
    string PaymentDetailId);

public sealed record SubscriptionInvoiceHistoryRecord(
    string PaymentDetailId,
    string ProviderName,
    string? OrderId,
    string? Description,
    decimal Amount,
    decimal RefundedAmount,
    string CurrencyCode,
    string Status,
    DateTime IssuedAtUtc);

public sealed record SubscriptionInvoiceHistoryPage(
    IReadOnlyList<SubscriptionInvoiceHistoryRecord> Items,
    bool HasMore);
