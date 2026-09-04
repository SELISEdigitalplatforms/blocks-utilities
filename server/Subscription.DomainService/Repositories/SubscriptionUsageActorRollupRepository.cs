using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageActorRollupRepository : ISubscriptionUsageActorRollupRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageActorRollupRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Rollups(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageActorRollupIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task ApplyAsync(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime dayUtc,
        string userId,
        decimal delta,
        DateTime recordedAtUtc,
        string sourceRecordId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var itemId = BucketId(tenantId, organizationId, meterKey, dayUtc, userId);
        var builder = Builders<SubscriptionUsageActorRollup>.Filter;

        // Same idempotency guard as the activity rollup: matches an existing bucket only when
        // this entry is newer than the last one folded in, so a re-run over an already-applied
        // page collides on _id instead of double-counting.
        var filter = builder.And(
            builder.Eq(rollup => rollup.ItemId, itemId),
            builder.Or(
                builder.Lt(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc),
                builder.And(
                    builder.Eq(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc),
                    builder.Lt(rollup => rollup.SourceCursorItemId, sourceRecordId))));

        var update = Builders<SubscriptionUsageActorRollup>.Update
            .SetOnInsert(rollup => rollup.ItemId, itemId)
            .SetOnInsert(rollup => rollup.TenantId, tenantId)
            .SetOnInsert(rollup => rollup.OrganizationId, organizationId)
            .SetOnInsert(rollup => rollup.MeterKey, meterKey)
            .SetOnInsert(rollup => rollup.DayUtc, dayUtc)
            .SetOnInsert(rollup => rollup.UserId, userId)
            .Set(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc)
            .Set(rollup => rollup.SourceCursorItemId, sourceRecordId)
            .Set(rollup => rollup.UpdatedAtUtc, updatedAtUtc)
            .Set(rollup => rollup.SchemaVersion, SubscriptionUsageActorRollup.CurrentSchemaVersion)
            .Inc(rollup => rollup.ConsumedQuantity, delta)
            .Inc(rollup => rollup.EntryCount, 1L);

        try
        {
            await Rollups(tenantId).UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Already accounted for by an earlier pass over the same page. See the activity
            // rollup's own remarks.
        }
    }

    public async Task<UsageActorRollupPage> ListAsync(
        string tenantId,
        string organizationId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageActorRollupCursor? after,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var builder = Builders<SubscriptionUsageActorRollup>.Filter;
        var filters = new List<FilterDefinition<SubscriptionUsageActorRollup>>
        {
            builder.Eq(rollup => rollup.TenantId, tenantId),
            builder.Eq(rollup => rollup.OrganizationId, organizationId)
        };

        if (!string.IsNullOrWhiteSpace(meterKey))
        {
            filters.Add(builder.Eq(rollup => rollup.MeterKey, meterKey));
        }

        if (fromUtc is { } from)
        {
            filters.Add(builder.Gte(rollup => rollup.DayUtc, from));
        }

        if (toUtc is { } to)
        {
            filters.Add(builder.Lte(rollup => rollup.DayUtc, to));
        }

        if (after is not null)
        {
            filters.Add(builder.Or(
                builder.Lt(rollup => rollup.DayUtc, after.DayUtc),
                builder.And(
                    builder.Eq(rollup => rollup.DayUtc, after.DayUtc),
                    builder.Gt(rollup => rollup.UserId, after.UserId))));
        }

        var items = await Rollups(tenantId)
            .Find(builder.And(filters))
            .Sort(Builders<SubscriptionUsageActorRollup>.Sort
                .Descending(rollup => rollup.DayUtc)
                .Ascending(rollup => rollup.UserId))
            .Limit(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new UsageActorRollupPage(items, hasMore);
    }

    internal static string BucketId(
        string tenantId,
        string organizationId,
        string meterKey,
        DateTime dayUtc,
        string userId) =>
        $"{tenantId}:{organizationId}:{meterKey}:{dayUtc:yyyyMMdd}:{userId}";

    private IMongoCollection<SubscriptionUsageActorRollup> Rollups(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageActorRollup>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageActorRollups);
}
