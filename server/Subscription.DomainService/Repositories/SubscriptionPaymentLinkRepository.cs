using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionPaymentLinkRepository : ISubscriptionPaymentLinkRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionPaymentLinkRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Links(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreatePaymentLinkIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryCreateAsync(
        SubscriptionPaymentLink link,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        try
        {
            await EnsureIndexesAsync(link.TenantId, cancellationToken);

            await Links(link.TenantId)
                .InsertOneAsync(link, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<SubscriptionPaymentLink?> FindByPaymentAsync(
        string tenantId,
        string paymentDetailId,
        CancellationToken cancellationToken) =>
        await Links(tenantId)
            .Find(Builders<SubscriptionPaymentLink>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.PaymentDetailId,
                    paymentDetailId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<SubscriptionPaymentLink?> FindBySubscriptionAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Links(tenantId)
            .Find(Builders<SubscriptionPaymentLink>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.SubscriptionId,
                    subscriptionId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionPaymentLink>> ListDueAsync(
        string tenantId,
        DateTime dueAtUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Links(tenantId)
            .Find(Builders<SubscriptionPaymentLink>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.State,
                    SubscriptionPaymentLinkState.Pending),
                Builders<SubscriptionPaymentLink>.Filter.Or(
                    Builders<SubscriptionPaymentLink>.Filter.Eq(
                        link => link.NextCheckAtUtc,
                        null),
                    Builders<SubscriptionPaymentLink>.Filter.Lte(
                        link => link.NextCheckAtUtc,
                        dueAtUtc))))
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<bool> TrySettleAsync(
        string tenantId,
        string linkId,
        SubscriptionPaymentLinkState state,
        CancellationToken cancellationToken)
    {
        var result = await Links(tenantId).UpdateOneAsync(
            Builders<SubscriptionPaymentLink>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.ItemId,
                    linkId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.State,
                    SubscriptionPaymentLinkState.Pending)),
            Builders<SubscriptionPaymentLink>.Update
                .Set(link => link.State, state)
                .Set(link => link.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task RescheduleAsync(
        string tenantId,
        string linkId,
        int attemptCount,
        DateTime nextCheckAtUtc,
        string? failureReason,
        CancellationToken cancellationToken) =>
        await Links(tenantId).UpdateOneAsync(
            Builders<SubscriptionPaymentLink>.Filter.And(
                TenantFilter(tenantId),
                Builders<SubscriptionPaymentLink>.Filter.Eq(
                    link => link.ItemId,
                    linkId)),
            Builders<SubscriptionPaymentLink>.Update
                .Set(link => link.AttemptCount, attemptCount)
                .Set(link => link.NextCheckAtUtc, nextCheckAtUtc)
                .Set(link => link.LastError, Shorten(failureReason))
                .Set(link => link.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

    private static FilterDefinition<SubscriptionPaymentLink> TenantFilter(string tenantId) =>
        Builders<SubscriptionPaymentLink>.Filter.Eq(
            link => link.TenantId,
            tenantId);

    private static string? Shorten(string? value) =>
        value is null || value.Length <= 500 ? value : value[..500];

    private IMongoCollection<SubscriptionPaymentLink> Links(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionPaymentLink>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.PaymentLinks);
}
