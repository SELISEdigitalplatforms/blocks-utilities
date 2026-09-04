using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageActivityRollupRepository :
    ISubscriptionUsageActivityRollupRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageActivityRollupRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Rollups(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageActivityRollupIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task ApplyAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        string meterKey,
        string planId,
        string planCode,
        DateTime dayUtc,
        int hourUtc,
        decimal delta,
        DateTime recordedAtUtc,
        string sourceRecordId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var itemId = BucketId(tenantId, organizationId, subscriptionId, meterKey, dayUtc);
        var builder = Builders<SubscriptionUsageActivityRollup>.Filter;

        // Matches an existing bucket only when this ledger entry is strictly newer than the last
        // one folded into it — the guard that makes a re-run of the same page idempotent. A
        // record already accounted for fails this filter, the upsert then collides on _id, and
        // that collision is the "already applied, nothing to do" signal caught below. A bucket
        // that does not exist yet fails the whole filter (there is no document to compare
        // against) and the upsert inserts it fresh instead.
        var filter = builder.And(
            builder.Eq(rollup => rollup.ItemId, itemId),
            builder.Or(
                builder.Lt(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc),
                builder.And(
                    builder.Eq(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc),
                    builder.Lt(rollup => rollup.SourceCursorItemId, sourceRecordId))));

        var update = Builders<SubscriptionUsageActivityRollup>.Update
            .SetOnInsert(rollup => rollup.ItemId, itemId)
            .SetOnInsert(rollup => rollup.TenantId, tenantId)
            .SetOnInsert(rollup => rollup.OrganizationId, organizationId)
            .SetOnInsert(rollup => rollup.SubscriptionId, subscriptionId)
            .SetOnInsert(rollup => rollup.MeterKey, meterKey)
            .SetOnInsert(rollup => rollup.DayUtc, dayUtc)
            .SetOnInsert(rollup => rollup.HourlyQuantity, new long[24])
            .Set(rollup => rollup.PlanId, planId)
            .Set(rollup => rollup.PlanCode, planCode)
            .Set(rollup => rollup.SourceCursorRecordedAtUtc, recordedAtUtc)
            .Set(rollup => rollup.SourceCursorItemId, sourceRecordId)
            .Set(rollup => rollup.UpdatedAtUtc, updatedAtUtc)
            .Set(rollup => rollup.SchemaVersion, SubscriptionUsageActivityRollup.CurrentSchemaVersion)
            .Inc(rollup => rollup.ConsumedQuantity, delta)
            .Inc(rollup => rollup.EntryCount, 1L)
            .Inc($"{nameof(SubscriptionUsageActivityRollup.HourlyQuantity)}.{hourUtc}", 1L);

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
            // The bucket already exists and this entry did not advance its cursor: it was already
            // folded in by an earlier pass over the same page. Nothing to do — applying it again
            // would double-count.
        }
    }

    public async Task<UsageActivityRollupPage> ListAsync(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageRollupCursor? after,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var builder = Builders<SubscriptionUsageActivityRollup>.Filter;
        var filters = new List<FilterDefinition<SubscriptionUsageActivityRollup>>
        {
            builder.Eq(rollup => rollup.TenantId, tenantId)
        };

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            filters.Add(builder.Eq(rollup => rollup.OrganizationId, organizationId));
        }

        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            filters.Add(builder.Eq(rollup => rollup.SubscriptionId, subscriptionId));
        }

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
                    builder.Lt(rollup => rollup.ItemId, after.ItemId))));
        }

        var items = await Rollups(tenantId)
            .Find(builder.And(filters))
            .Sort(Builders<SubscriptionUsageActivityRollup>.Sort
                .Descending(rollup => rollup.DayUtc)
                .Descending(rollup => rollup.ItemId))
            .Limit(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new UsageActivityRollupPage(items, hasMore);
    }

    public async Task<UsageTimeseriesPage> SumByPeriodAsync(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        BillingInterval granularity,
        int pageSize,
        DateTime? afterPeriodStartUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var matchFilter = BuildFilter(
            tenantId, organizationId, subscriptionId, meterKey, fromUtc, toUtc);

        var aggregate = Rollups(tenantId).Aggregate().Match(matchFilter)
            .AppendStage<BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                {
                    "_id", new BsonDocument("$dateTrunc", new BsonDocument
                    {
                        { "date", "$DayUtc" },
                        { "unit", MongoUnitFor(granularity) },
                        { "timezone", "UTC" },
                        { "startOfWeek", "monday" }
                    })
                },
                { "ConsumedQuantity", new BsonDocument("$sum", "$ConsumedQuantity") },
                { "EntryCount", new BsonDocument("$sum", "$EntryCount") }
            }));

        if (afterPeriodStartUtc is { } after)
        {
            aggregate = aggregate.AppendStage<BsonDocument>(
                new BsonDocument("$match", new BsonDocument("_id", new BsonDocument("$lt", after))));
        }

        aggregate = aggregate
            .AppendStage<BsonDocument>(new BsonDocument("$sort", new BsonDocument("_id", -1)))
            .AppendStage<BsonDocument>(new BsonDocument("$limit", pageSize + 1));

        var documents = await aggregate.ToListAsync(cancellationToken);
        var hasMore = documents.Count > pageSize;

        if (hasMore)
        {
            documents.RemoveAt(documents.Count - 1);
        }

        var items = documents
            .Select(document => new UsageTimeseriesBucket(
                document["_id"].ToUniversalTime(),
                document["ConsumedQuantity"].ToDecimal(),
                document["EntryCount"].ToInt64()))
            .ToList();

        return new UsageTimeseriesPage(items, hasMore);
    }

    public async Task<UsageOrganizationTotalsPage> SumByOrganizationAsync(
        string tenantId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc,
        int pageSize,
        UsageOrganizationTotalsCursor? after,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var matchFilter = BuildFilter(
            tenantId, organizationId: null, subscriptionId, meterKey, fromUtc, toUtc);

        var aggregate = Rollups(tenantId).Aggregate().Match(matchFilter)
            .AppendStage<BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$OrganizationId" },
                { "ConsumedQuantity", new BsonDocument("$sum", "$ConsumedQuantity") },
                { "EntryCount", new BsonDocument("$sum", "$EntryCount") }
            }));

        if (after is { } cursor)
        {
            aggregate = aggregate.AppendStage<BsonDocument>(new BsonDocument("$match", new BsonDocument(
                "$or",
                new BsonArray
                {
                    new BsonDocument("ConsumedQuantity", new BsonDocument("$lt", cursor.ConsumedQuantity)),
                    new BsonDocument("$and", new BsonArray
                    {
                        new BsonDocument("ConsumedQuantity", cursor.ConsumedQuantity),
                        new BsonDocument("_id", new BsonDocument("$gt", cursor.OrganizationId))
                    })
                })));
        }

        aggregate = aggregate
            .AppendStage<BsonDocument>(new BsonDocument("$sort", new BsonDocument
            {
                { "ConsumedQuantity", -1 },
                { "_id", 1 }
            }))
            .AppendStage<BsonDocument>(new BsonDocument("$limit", pageSize + 1));

        var documents = await aggregate.ToListAsync(cancellationToken);
        var hasMore = documents.Count > pageSize;

        if (hasMore)
        {
            documents.RemoveAt(documents.Count - 1);
        }

        var items = documents
            .Select(document => new UsageOrganizationTotal(
                document["_id"].AsString,
                document["ConsumedQuantity"].ToDecimal(),
                document["EntryCount"].ToInt64()))
            .ToList();

        return new UsageOrganizationTotalsPage(items, hasMore);
    }

    private static FilterDefinition<SubscriptionUsageActivityRollup> BuildFilter(
        string tenantId,
        string? organizationId,
        string? subscriptionId,
        string? meterKey,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var builder = Builders<SubscriptionUsageActivityRollup>.Filter;
        var filters = new List<FilterDefinition<SubscriptionUsageActivityRollup>>
        {
            builder.Eq(rollup => rollup.TenantId, tenantId)
        };

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            filters.Add(builder.Eq(rollup => rollup.OrganizationId, organizationId));
        }

        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            filters.Add(builder.Eq(rollup => rollup.SubscriptionId, subscriptionId));
        }

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

        return builder.And(filters);
    }

    private static string MongoUnitFor(BillingInterval granularity) => granularity switch
    {
        BillingInterval.Day => "day",
        BillingInterval.Week => "week",
        BillingInterval.Month => "month",
        BillingInterval.Year => "year",
        _ => "month"
    };

    internal static string BucketId(
        string tenantId,
        string organizationId,
        string subscriptionId,
        string meterKey,
        DateTime dayUtc) =>
        $"{tenantId}:{organizationId}:{subscriptionId}:{meterKey}:{dayUtc:yyyyMMdd}";

    private IMongoCollection<SubscriptionUsageActivityRollup> Rollups(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageActivityRollup>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageActivityRollups);
}
