using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionEntitlementsCurrentRepository : ISubscriptionEntitlementsCurrentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionEntitlementsCurrentRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Current(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateEntitlementsCurrentIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryPublishAsync(
        SubscriptionEntitlementsCurrent document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        var collection = Current(document.TenantId);

        // One version to order by, so a plain conditional replace suffices -- unlike the usage
        // projection, nothing here moves on any track but the subscription's own version.
        var filter = Builders<SubscriptionEntitlementsCurrent>.Filter.And(
            Builders<SubscriptionEntitlementsCurrent>.Filter.Eq(
                current => current.ItemId,
                document.ItemId),
            Builders<SubscriptionEntitlementsCurrent>.Filter.Lt(
                current => current.SubscriptionVersion,
                document.SubscriptionVersion));

        var result = await collection.ReplaceOneAsync(
            filter,
            document,
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return true;
        }

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Somebody published between the replace above and this insert. One more try, since it
            // may now have something newer to contribute; if it does not, it correctly changes
            // nothing.
            var retried = await collection.ReplaceOneAsync(
                filter,
                document,
                cancellationToken: cancellationToken);

            return retried.ModifiedCount == 1;
        }
    }

    public async Task<SubscriptionEntitlementsCurrent?> GetAsync(
        string tenantId,
        string subscriptionId,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionEntitlementsCurrent>.Filter.Eq(
                current => current.ItemId,
                subscriptionId))
            .FirstOrDefaultAsync(cancellationToken);

    private IMongoCollection<SubscriptionEntitlementsCurrent> Current(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionEntitlementsCurrent>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.EntitlementsCurrent);
}
