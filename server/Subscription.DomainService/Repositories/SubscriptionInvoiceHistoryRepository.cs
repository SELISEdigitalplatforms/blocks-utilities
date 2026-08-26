using System.Text.RegularExpressions;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Reads paid subscription invoices by subscriber organization.
/// </summary>
/// <remarks>
/// Renewal payments carry two organizations: <c>OrganizationId</c> is the merchant scope that
/// took the money, while <c>CustomerOrganizationId</c> is the subscriber that owns the invoice.
/// An invoice history query must use the latter or console-mediated subscriptions are exposed
/// to the wrong organization.
/// </remarks>
public sealed class SubscriptionInvoiceHistoryRepository :
    ISubscriptionInvoiceHistoryRepository
{
    private static readonly string[] SettledStatuses =
    [
        PaymentStatuses.Captured,
        PaymentStatuses.PartiallyRefunded,
        PaymentStatuses.Refunded
    ];

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IPaymentRepository _payments;

    public SubscriptionInvoiceHistoryRepository(
        IDbContextProvider dbContextProvider,
        IPaymentRepository payments)
    {
        _dbContextProvider = dbContextProvider;
        _payments = payments;
    }

    public async Task<SubscriptionInvoiceHistoryPage> ListAsync(
        string tenantId,
        string organizationId,
        int pageSize,
        SubscriptionInvoiceHistoryCursor? after,
        CancellationToken cancellationToken)
    {
        await _payments.EnsureIndexesAsync(tenantId, cancellationToken);

        var records = await Collection(tenantId)
            .Find(BuildFilter(tenantId, organizationId, after))
            .Sort(Builders<PaymentDetail>.Sort
                .Descending(payment => payment.PaymentDate)
                .Descending(payment => payment.ItemId))
            .Limit(pageSize + 1)
            .Project(payment => new SubscriptionInvoiceHistoryRecord(
                payment.ItemId,
                payment.ProviderName,
                payment.OrderId,
                payment.Description,
                payment.PreciseAmount,
                payment.RefundedAmount,
                payment.CurrencyCode,
                payment.PaymentStatus,
                payment.PaymentDate,
                payment.SubscriptionNetAmountMinor,
                payment.SubscriptionTaxAmountMinor,
                payment.SubscriptionCreditAmountMinor,
                payment.SubscriptionTaxRateBasisPoints,
                payment.SubscriptionTaxMode,
                payment.SubscriptionGrossAmountMinor,
                payment.SubscriptionBuiltInDiscountMinor,
                payment.SubscriptionPromotionalDiscountMinor,
                payment.SubscriptionAutomaticDiscountBasisPoints,
                payment.SubscriptionQuantityDiscountBasisPoints,
                payment.SubscriptionDiscountCombination,
                payment.SubscriptionSettlement))
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > pageSize;
        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        return new SubscriptionInvoiceHistoryPage(records, hasMore);
    }

    public async Task<IReadOnlyList<SubscriptionInvoiceHistoryRecord>> ListBySubscriptionAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken)
    {
        await _payments.EnsureIndexesAsync(tenantId, cancellationToken);

        // Every order id this subscription's charges ever used shares this prefix — the bare
        // form for the initial charge, a colon-suffixed form for every renewal, plan change,
        // quantity settlement and usage invoice since (see SubscriptionConstants). A prefix
        // match therefore finds all of them without knowing which kinds exist yet.
        var orderIdPrefix = SubscriptionConstants.OrderIdFor(subscriptionId);

        var filter = Builders<PaymentDetail>.Filter.And(
            BuildFilter(tenantId, organizationId, after: null),
            Builders<PaymentDetail>.Filter.Regex(
                payment => payment.OrderId,
                new BsonRegularExpression($"^{Regex.Escape(orderIdPrefix)}")));

        return await Collection(tenantId)
            .Find(filter)
            .Sort(Builders<PaymentDetail>.Sort
                .Descending(payment => payment.PaymentDate)
                .Descending(payment => payment.ItemId))
            .Limit(limit)
            .Project(payment => new SubscriptionInvoiceHistoryRecord(
                payment.ItemId,
                payment.ProviderName,
                payment.OrderId,
                payment.Description,
                payment.PreciseAmount,
                payment.RefundedAmount,
                payment.CurrencyCode,
                payment.PaymentStatus,
                payment.PaymentDate,
                payment.SubscriptionNetAmountMinor,
                payment.SubscriptionTaxAmountMinor,
                payment.SubscriptionCreditAmountMinor,
                payment.SubscriptionTaxRateBasisPoints,
                payment.SubscriptionTaxMode,
                payment.SubscriptionGrossAmountMinor,
                payment.SubscriptionBuiltInDiscountMinor,
                payment.SubscriptionPromotionalDiscountMinor,
                payment.SubscriptionAutomaticDiscountBasisPoints,
                payment.SubscriptionQuantityDiscountBasisPoints,
                payment.SubscriptionDiscountCombination,
                payment.SubscriptionSettlement))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionSettledChargeRecord>> ListSettledSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        string? afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        await _payments.EnsureIndexesAsync(tenantId, cancellationToken);

        // Every charge this module raises carries the prefix, whatever kind it is, so one prefix
        // match finds all of them and no payment from another product in the tenant.
        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(payment => payment.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.In(
                payment => payment.PaymentStatus,
                SettledStatuses),
            After(sinceUtc, afterId, payment => payment.PaymentDate),
            Builders<PaymentDetail>.Filter.Regex(
                payment => payment.OrderId,
                new BsonRegularExpression(
                    $"^{Regex.Escape(SubscriptionConstants.OrderIdPrefix)}")));

        // Oldest first, so a backlog is worked through in the order it arose rather than the newest
        // charges repeatedly starving the ones that have been waiting. Tie-broken by id, which is what
        // makes the ordering total and the paging able to resume without overlap or omission.
        return await Collection(tenantId)
            .Find(filter)
            .SortBy(payment => payment.PaymentDate)
            .ThenBy(payment => payment.ItemId)
            .Limit(limit)
            .Project(payment => new SubscriptionSettledChargeRecord(
                payment.ItemId,
                payment.OrderId,
                payment.PaymentDate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Everything strictly after a page position, or from an instant inclusively when there is none.
    /// </summary>
    /// <remarks>
    /// A keyset page over <c>(instant, id)</c>. The alternative — an instant with an overlap subtracted
    /// from it — re-reads a slice of history on every pass and still cannot make progress when more
    /// records share one instant than a page can hold.
    /// </remarks>
    private static FilterDefinition<PaymentDetail> After(
        DateTime sinceUtc,
        string? afterId,
        System.Linq.Expressions.Expression<Func<PaymentDetail, DateTime>> instant) =>
        afterId is not { Length: > 0 }
            ? Builders<PaymentDetail>.Filter.Gte(instant, sinceUtc)
            : Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Gt(instant, sinceUtc),
                Builders<PaymentDetail>.Filter.And(
                    Builders<PaymentDetail>.Filter.Eq(instant, sinceUtc),
                    Builders<PaymentDetail>.Filter.Gt(payment => payment.ItemId, afterId)));

    public async Task<IReadOnlyList<SubscriptionRefundedChargeRecord>> ListRefundedSinceAsync(
        string tenantId,
        DateTime sinceUtc,
        string? afterId,
        int limit,
        CancellationToken cancellationToken)
    {
        await _payments.EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<PaymentDetail>.Filter.And(
            Builders<PaymentDetail>.Filter.Eq(payment => payment.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.In(
                payment => payment.PaymentStatus,
                new[] { PaymentStatuses.PartiallyRefunded, PaymentStatuses.Refunded }),
            After(sinceUtc, afterId, payment => payment.LastUpdatedDateUtc),
            Builders<PaymentDetail>.Filter.Regex(
                payment => payment.OrderId,
                new BsonRegularExpression(
                    $"^{Regex.Escape(SubscriptionConstants.OrderIdPrefix)}")));

        var payments = await Collection(tenantId)
            .Find(filter)
            .SortBy(payment => payment.LastUpdatedDateUtc)
            .ThenBy(payment => payment.ItemId)
            .Limit(limit)
            .Project(payment => new
            {
                payment.ItemId,
                payment.Refunds,
                payment.LastUpdatedDateUtc
            })
            .ToListAsync(cancellationToken);

        // Only the refunds that actually returned money. One that is submitted, failed or reversed
        // has moved nothing, and a credit note for it would be a promise the bank did not keep.
        return
        [
            .. payments.Select(payment => new SubscriptionRefundedChargeRecord(
                payment.ItemId,
                [
                    .. payment.Refunds
                        .Where(refund => string.Equals(
                            refund.Status,
                            PaymentRefundStatuses.Succeeded,
                            StringComparison.Ordinal))
                        .Select(refund => refund.RefundId)
                ],
                payment.LastUpdatedDateUtc))
        ];
    }

    public static FilterDefinition<PaymentDetail> BuildFilter(
        string tenantId,
        string organizationId,
        SubscriptionInvoiceHistoryCursor? after)
    {
        var filters = new List<FilterDefinition<PaymentDetail>>
        {
            Builders<PaymentDetail>.Filter.Eq(payment => payment.TenantId, tenantId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.CustomerOrganizationId,
                organizationId),
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.PaymentFlow,
                PaymentFlows.SubscriptionInvoice),
            Builders<PaymentDetail>.Filter.In(
                payment => payment.PaymentStatus,
                SettledStatuses),
            Builders<PaymentDetail>.Filter.Exists(
                payment => payment.ProviderInvoiceId,
                true),
            Builders<PaymentDetail>.Filter.Ne(
                payment => payment.ProviderInvoiceId,
                null),
            Builders<PaymentDetail>.Filter.Ne(
                payment => payment.ProviderInvoiceId,
                string.Empty)
        };

        if (after is not null)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Or(
                    Builders<PaymentDetail>.Filter.Lt(
                        payment => payment.PaymentDate,
                        after.IssuedAtUtc),
                    Builders<PaymentDetail>.Filter.And(
                        Builders<PaymentDetail>.Filter.Eq(
                            payment => payment.PaymentDate,
                            after.IssuedAtUtc),
                        Builders<PaymentDetail>.Filter.Lt(
                            payment => payment.ItemId,
                            after.PaymentDetailId))));
        }

        return Builders<PaymentDetail>.Filter.And(filters);
    }

    private IMongoCollection<PaymentDetail> Collection(string tenantId) =>
        _dbContextProvider
            .GetDatabase(Require(tenantId, nameof(tenantId)))
            .GetCollection<PaymentDetail>("PaymentDetails");

    private static string Require(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A non-empty value is required.", parameterName);
}
