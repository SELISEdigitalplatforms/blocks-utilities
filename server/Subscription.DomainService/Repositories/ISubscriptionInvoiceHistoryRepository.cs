namespace Subscription.DomainService.Repositories;

public interface ISubscriptionInvoiceHistoryRepository
{
    Task<SubscriptionInvoiceHistoryPage> ListAsync(
        string tenantId,
        string organizationId,
        int pageSize,
        SubscriptionInvoiceHistoryCursor? after,
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
