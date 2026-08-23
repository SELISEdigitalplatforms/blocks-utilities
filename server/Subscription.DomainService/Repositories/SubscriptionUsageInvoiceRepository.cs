using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageInvoiceRepository : ISubscriptionUsageInvoiceRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageInvoiceRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Invoices(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageInvoiceIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryCreateAsync(
        SubscriptionUsageInvoice invoice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        try
        {
            await EnsureIndexesAsync(invoice.TenantId, cancellationToken);

            await Invoices(invoice.TenantId)
                .InsertOneAsync(invoice, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<SubscriptionUsageInvoice?> GetAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken) =>
        await Invoices(tenantId)
            .Find(Builders<SubscriptionUsageInvoice>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionUsageInvoice>.Filter.Eq(
                    invoice => invoice.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageInvoice>.Filter.Eq(
                    invoice => invoice.PeriodKey,
                    periodKey)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUsageInvoice>> ListBySubscriptionAsync(
        string tenantId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken) =>
        await Invoices(tenantId)
            .Find(Builders<SubscriptionUsageInvoice>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionUsageInvoice>.Filter.Eq(
                    invoice => invoice.SubscriptionId,
                    subscriptionId)))
            .SortByDescending(invoice => invoice.PeriodKey)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUsageInvoice>> ListDueAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Invoices(tenantId)
            .Find(Builders<SubscriptionUsageInvoice>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionUsageInvoice>.Filter.Eq(
                    invoice => invoice.State,
                    SubscriptionUsageInvoiceState.Pending),
                Builders<SubscriptionUsageInvoice>.Filter.Lte(
                    invoice => invoice.NextAttemptAtUtc,
                    dueAtUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryMarkChargedAsync(
        string tenantId,
        string invoiceId,
        string paymentDetailId,
        CancellationToken cancellationToken)
    {
        var result = await Invoices(tenantId).UpdateOneAsync(
            PendingFilter(tenantId, invoiceId),
            Builders<SubscriptionUsageInvoice>.Update
                .Set(invoice => invoice.State, SubscriptionUsageInvoiceState.Charged)
                .Set(invoice => invoice.PaymentDetailId, paymentDetailId)
                .Set(invoice => invoice.NextAttemptAtUtc, null)
                .Set(invoice => invoice.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryMarkNoChargeAsync(
        string tenantId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var result = await Invoices(tenantId).UpdateOneAsync(
            PendingFilter(tenantId, invoiceId),
            Builders<SubscriptionUsageInvoice>.Update
                .Set(invoice => invoice.State, SubscriptionUsageInvoiceState.NoCharge)
                .Set(invoice => invoice.NextAttemptAtUtc, null)
                .Set(invoice => invoice.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryMarkAbandonedAsync(
        string tenantId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var result = await Invoices(tenantId).UpdateOneAsync(
            PendingFilter(tenantId, invoiceId),
            Builders<SubscriptionUsageInvoice>.Update
                .Set(invoice => invoice.State, SubscriptionUsageInvoiceState.Abandoned)
                .Set(invoice => invoice.NextAttemptAtUtc, null)
                .Set(invoice => invoice.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task RescheduleAsync(
        string tenantId,
        string invoiceId,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string? failureReason,
        CancellationToken cancellationToken) =>
        await Invoices(tenantId).UpdateOneAsync(
            Builders<SubscriptionUsageInvoice>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionUsageInvoice>.Filter.Eq(
                    invoice => invoice.ItemId,
                    invoiceId)),
            Builders<SubscriptionUsageInvoice>.Update
                .Set(invoice => invoice.AttemptCount, attemptCount)
                .Set(invoice => invoice.NextAttemptAtUtc, nextAttemptAtUtc)
                .Set(invoice => invoice.LastError, Shorten(failureReason))
                .Set(invoice => invoice.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

    private static FilterDefinition<SubscriptionUsageInvoice> PendingFilter(
        string tenantId,
        string invoiceId) =>
        Builders<SubscriptionUsageInvoice>.Filter.And(
            TenantFilter(tenantId),
            Builders<SubscriptionUsageInvoice>.Filter.Eq(
                invoice => invoice.ItemId,
                invoiceId),
            Builders<SubscriptionUsageInvoice>.Filter.Eq(
                invoice => invoice.State,
                SubscriptionUsageInvoiceState.Pending));

    private static FilterDefinition<SubscriptionUsageInvoice> TenantFilter(string tenantId) =>
        Builders<SubscriptionUsageInvoice>.Filter.Eq(
            invoice => invoice.TenantId,
            tenantId);

    private static string? Shorten(string? value) =>
        value is null || value.Length <= 500 ? value : value[..500];

    private IMongoCollection<SubscriptionUsageInvoice> Invoices(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageInvoice>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageInvoices);
}
