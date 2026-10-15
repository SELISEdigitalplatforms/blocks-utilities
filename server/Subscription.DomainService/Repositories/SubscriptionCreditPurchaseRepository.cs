using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>The Mongo-backed credit purchase records.</summary>
public sealed class SubscriptionCreditPurchaseRepository : ISubscriptionCreditPurchaseRepository
{
    private const int DuplicateKeyErrorCode = 11000;

    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionCreditPurchaseRepository(IDbContextProvider db) => _db = db;

    public async Task<bool> TryCreateAsync(
        SubscriptionCreditPurchase purchase,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        await EnsureIndexesAsync(purchase.TenantId, cancellationToken);

        try
        {
            await Collection(purchase.TenantId)
                .InsertOneAsync(purchase, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // The id is the subscription and the idempotency key, so this is a retry: the caller
            // reads the stored record back and replays it.
            return false;
        }
    }

    public async Task<SubscriptionCreditPurchase?> GetAsync(
        string tenantId,
        string purchaseId,
        CancellationToken cancellationToken) =>
        await Collection(tenantId)
            .Find(purchase => purchase.TenantId == tenantId && purchase.ItemId == purchaseId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAdvanceAsync(
        SubscriptionCreditPurchase purchase,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        purchase.Version = expectedVersion + 1;

        var result = await Collection(purchase.TenantId).ReplaceOneAsync(
            stored => stored.TenantId == purchase.TenantId &&
                      stored.ItemId == purchase.ItemId &&
                      stored.Version == expectedVersion,
            purchase,
            cancellationToken: cancellationToken);

        if (result.ModifiedCount > 0)
        {
            return true;
        }

        purchase.Version = expectedVersion;
        return false;
    }

    public async Task<IReadOnlyList<SubscriptionCreditPurchase>> ListAsync(
        string tenantId,
        string subscriptionId,
        CreditPurchaseStatus? status,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditPurchase>.Filter;
        var clauses = filter.And(
            filter.Eq(purchase => purchase.TenantId, tenantId),
            filter.Eq(purchase => purchase.SubscriptionId, subscriptionId));

        if (status is { } wanted)
        {
            clauses &= filter.Eq(purchase => purchase.Status, wanted);
        }

        return await Collection(tenantId)
            .Find(clauses)
            .SortByDescending(purchase => purchase.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionCreditPurchase>> ListStaleAsync(
        string tenantId,
        IReadOnlyCollection<CreditPurchaseStatus> statuses,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionCreditPurchase>.Filter;

        return await Collection(tenantId)
            .Find(filter.And(
                filter.Eq(purchase => purchase.TenantId, tenantId),
                filter.In(purchase => purchase.Status, statuses),
                filter.Lt(purchase => purchase.LastUpdatedAtUtc, olderThanUtc)))
            .SortBy(purchase => purchase.LastUpdatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionCreditIndexDefinitions.CreatePurchaseIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private static bool IsDuplicateKey(Exception exception) =>
        exception switch
        {
            MongoWriteException write =>
                write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
            MongoCommandException command => command.Code == DuplicateKeyErrorCode,
            _ => false
        };

    private IMongoCollection<SubscriptionCreditPurchase> Collection(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionCreditPurchase>(
            _db, tenantId, SubscriptionCollections.CreditPurchases);
}
