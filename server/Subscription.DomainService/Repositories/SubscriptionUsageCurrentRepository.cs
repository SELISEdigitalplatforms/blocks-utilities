using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageCurrentRepository : ISubscriptionUsageCurrentRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageCurrentRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Current(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageCurrentIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryPublishAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        // Newer-only, and upsert. The two together are what make this safe to call from concurrent
        // recordings and from a repair at the same time: whoever holds the highest version wins, and
        // the first of them to arrive creates the document.
        var filter = Builders<SubscriptionUsageCurrent>.Filter.And(
            Builders<SubscriptionUsageCurrent>.Filter.Eq(
                current => current.ItemId,
                document.ItemId),
            Builders<SubscriptionUsageCurrent>.Filter.Lt(
                current => current.SourceVersion,
                document.SourceVersion));

        var update = Builders<SubscriptionUsageCurrent>.Update
            .Set(current => current.TenantId, document.TenantId)
            .Set(current => current.OrganizationId, document.OrganizationId)
            .Set(current => current.SubscriptionId, document.SubscriptionId)
            .Set(current => current.SubscriptionStatus, document.SubscriptionStatus)
            .Set(current => current.PlanId, document.PlanId)
            .Set(current => current.PlanCode, document.PlanCode)
            .Set(current => current.MeterKey, document.MeterKey)
            .Set(current => current.UnitLabel, document.UnitLabel)
            .Set(current => current.PeriodKey, document.PeriodKey)
            .Set(current => current.PeriodStartUtc, document.PeriodStartUtc)
            .Set(current => current.PeriodEndUtc, document.PeriodEndUtc)
            .Set(current => current.Included, document.Included)
            .Set(current => current.Used, document.Used)
            .Set(current => current.Remaining, document.Remaining)
            .Set(current => current.Overage, document.Overage)
            .Set(current => current.OverageAllowed, document.OverageAllowed)
            .Set(current => current.SourceVersion, document.SourceVersion)
            .Set(current => current.SchemaVersion, document.SchemaVersion)
            .Set(current => current.UpdatedAtUtc, document.UpdatedAtUtc)
            .Set(current => current.ExpiresAtUtc, document.ExpiresAtUtc);

        var collection = Current(document.TenantId);

        // Update first, insert only if there is nothing to update — rather than one upsert.
        //
        // An upsert here would be wrong in the ordinary case, not just slower: when the stored
        // document is already NEWER the filter matches nothing, so Mongo would try to insert, and the
        // insert carries this document's own composed _id. Every stale publish — the expected outcome
        // whenever two recordings overlap — would raise a duplicate-key exception. Splitting the two
        // makes losing the version race a plain "modified nothing", and leaves exceptions for the one
        // case that genuinely is a race: two callers creating the same missing document at once.
        if (await UpdateIfNewerAsync(collection, filter, update, cancellationToken))
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
            // Somebody inserted it between the update above and this insert. It may have landed at a
            // version below this one, so the conditional update is attempted once more; if it is at
            // or beyond this version, that attempt correctly modifies nothing.
            //
            // Once, not in a loop: the document exists from here on, so a further attempt could only
            // lose the same version comparison again.
            return await UpdateIfNewerAsync(collection, filter, update, cancellationToken);
        }
    }

    private static async Task<bool> UpdateIfNewerAsync(
        IMongoCollection<SubscriptionUsageCurrent> collection,
        FilterDefinition<SubscriptionUsageCurrent> filter,
        UpdateDefinition<SubscriptionUsageCurrent> update,
        CancellationToken cancellationToken)
    {
        var result = await collection.UpdateOneAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TrySeedAsync(
        SubscriptionUsageCurrent document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await EnsureIndexesAsync(document.TenantId, cancellationToken);

        try
        {
            await Current(document.TenantId).InsertOneAsync(
                document,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already published, by a real recording or by an earlier seed. Nothing to do, and
            // deliberately nothing written: a seed that overwrote a live balance with zero would
            // discard usage somebody has been billed for.
            return false;
        }
    }

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListCurrentAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        DateTime asOfUtc,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.OrganizationId,
                    organizationId),
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageCurrent>.Filter.Lte(
                    current => current.PeriodStartUtc,
                    asOfUtc),
                // Strictly after: a period ends the instant its successor begins, so an inclusive
                // upper bound would return two windows for one meter at the boundary.
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    asOfUtc)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SubscriptionUsageCurrent>> ListBehindCountersAsync(
        string tenantId,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.And(
                Builders<SubscriptionUsageCurrent>.Filter.Eq(
                    current => current.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCurrent>.Filter.Lte(
                    current => current.PeriodStartUtc,
                    asOfUtc),
                Builders<SubscriptionUsageCurrent>.Filter.Gt(
                    current => current.PeriodEndUtc,
                    asOfUtc)))
            // Served by ix_usage_current_tenant_updated, which is descending on UpdatedAtUtc; the
            // ascending sort here walks the same index backwards rather than sorting in memory.
            .SortBy(current => current.UpdatedAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public async Task<SubscriptionUsageCurrent?> GetAsync(
        string tenantId,
        string documentId,
        CancellationToken cancellationToken) =>
        await Current(tenantId)
            .Find(Builders<SubscriptionUsageCurrent>.Filter.Eq(
                current => current.ItemId,
                documentId))
            .FirstOrDefaultAsync(cancellationToken);

    private IMongoCollection<SubscriptionUsageCurrent> Current(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageCurrent>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageCurrent);
}
