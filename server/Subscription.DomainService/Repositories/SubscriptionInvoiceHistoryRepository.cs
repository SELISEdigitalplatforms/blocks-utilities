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
