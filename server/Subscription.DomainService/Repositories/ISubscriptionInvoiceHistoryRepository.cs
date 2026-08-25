using Payment.DomainService.Entities;

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

    /// <summary>
    /// Every settled subscription charge in the tenant since an instant, across all organizations.
    /// </summary>
    /// <remarks>
    /// The recovery path for financial documents. Issuing is normally scheduled by the money path as
    /// it settles, but that scheduling write lives in another database and can be lost — so something
    /// has to be able to find a settled charge nobody queued, and the only place that knowledge exists
    /// is the payment collection.
    /// <para>
    /// Unscoped by organization on purpose, and therefore never reachable from a request: this is
    /// called by the worker with a tenant it was handed by the scheduler. The organization-scoped
    /// reads above are the ones a caller can ask for.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SubscriptionSettledChargeRecord>> ListSettledSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Subscription charges that have been refunded and touched since an instant.
    /// </summary>
    /// <remarks>
    /// Keyed on when the payment was last written rather than on when it was taken, because a refund
    /// happens long after the charge — often months. A window over payment dates would never see it.
    /// <para>
    /// This is how a credit note reaches the subscription module at all. A refund confirms inside the
    /// payment module, which must never depend on subscriptions, so nothing there can call this side;
    /// the subscription module has to come and look. That is a deliberate cost of keeping the
    /// dependency one-directional.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SubscriptionRefundedChargeRecord>> ListRefundedSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>A refunded subscription charge, and which of its refunds actually returned money.</summary>
public sealed record SubscriptionRefundedChargeRecord(
    string PaymentDetailId,
    IReadOnlyList<string> SucceededRefundIds);

/// <summary>
/// The minimum needed to decide whether a settled charge has its document yet.
/// </summary>
/// <remarks>
/// Deliberately not the whole payment. The sweep reads these in batches across every organization in
/// a tenant, and pulling amounts, instruments and provider payloads it will not look at would make a
/// recovery pass cost more than the work it recovers.
/// </remarks>
public sealed record SubscriptionSettledChargeRecord(
    string PaymentDetailId,
    string? OrderId,
    DateTime SettledAtUtc);

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
    DateTime IssuedAtUtc,
    long? NetAmountMinor = null,
    long? TaxAmountMinor = null,
    long? CreditAmountMinor = null,
    int? TaxRateBasisPoints = null,
    string? TaxMode = null,
    long? GrossAmountMinor = null,
    long? BuiltInDiscountMinor = null,
    long? PromotionalDiscountMinor = null,
    int? AutomaticDiscountBasisPoints = null,
    int? QuantityDiscountBasisPoints = null,
    string? DiscountCombination = null,
    SubscriptionSettlementBreakdown? Settlement = null);

public sealed record SubscriptionInvoiceHistoryPage(
    IReadOnlyList<SubscriptionInvoiceHistoryRecord> Items,
    bool HasMore);
