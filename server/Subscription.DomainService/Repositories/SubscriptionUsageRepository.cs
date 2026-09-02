using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionUsageRepository : ISubscriptionUsageRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionUsageRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Records(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageRecordIndexes(),
            cancellationToken);

        await Counters(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsageCounterIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<bool> TryAppendRecordAsync(
        SubscriptionUsageRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await EnsureIndexesAsync(record.TenantId, cancellationToken);

            await Records(record.TenantId)
                .InsertOneAsync(record, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<SubscriptionUsageCounter> ApplyDeltaAsync(
        SubscriptionUsageCounter seed,
        decimal delta,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var update = Builders<SubscriptionUsageCounter>.Update
            .Inc(counter => counter.Balance, delta)
            .Inc(counter => counter.AppliedRecordCount, 1)
            .Set(counter => counter.LastUpdatedAtUtc, DateTime.UtcNow)
            .SetOnInsert(counter => counter.TenantId, seed.TenantId)
            .SetOnInsert(counter => counter.OrganizationId, seed.OrganizationId)
            .SetOnInsert(counter => counter.SubscriptionId, seed.SubscriptionId)
            .SetOnInsert(counter => counter.MeterKey, seed.MeterKey)
            .SetOnInsert(counter => counter.PeriodKey, seed.PeriodKey)
            .SetOnInsert(counter => counter.PeriodStartUtc, seed.PeriodStartUtc)
            .SetOnInsert(counter => counter.PeriodEndUtc, seed.PeriodEndUtc)
            .SetOnInsert(counter => counter.ExpiresAtUtc, seed.ExpiresAtUtc)
            .SetOnInsert(counter => counter.LimitSnapshot, seed.LimitSnapshot)
            .SetOnInsert(counter => counter.NotifiedThresholds, []);

        // ReturnDocument.After is what makes this an enforcement point rather than a check:
        // the balance that comes back already includes this caller's delta, so two callers at
        // the boundary get different answers and only one of them is over.
        return await Counters(seed.TenantId).FindOneAndUpdateAsync(
            Builders<SubscriptionUsageCounter>.Filter.Eq(
                counter => counter.ItemId,
                seed.ItemId),
            update,
            new FindOneAndUpdateOptions<SubscriptionUsageCounter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task<SubscriptionUsageCounter?> GetCounterAsync(
        string tenantId,
        string counterId,
        CancellationToken cancellationToken) =>
        await Counters(tenantId)
            .Find(Builders<SubscriptionUsageCounter>.Filter.Eq(
                counter => counter.ItemId,
                counterId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, SubscriptionUsageCounter>> GetCountersAsync(
        string tenantId,
        IReadOnlyCollection<string> counterIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counterIds);

        if (counterIds.Count == 0)
        {
            return new Dictionary<string, SubscriptionUsageCounter>(StringComparer.Ordinal);
        }

        var counters = await Counters(tenantId)
            .Find(Builders<SubscriptionUsageCounter>.Filter.And(
                Builders<SubscriptionUsageCounter>.Filter.Eq(
                    counter => counter.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCounter>.Filter.In(
                    counter => counter.ItemId,
                    counterIds)))
            .ToListAsync(cancellationToken);

        // Keyed on the id the caller asked with, so a caller holding a meter and period can find its
        // counter without re-composing anything.
        return counters.ToDictionary(
            counter => counter.ItemId,
            counter => counter,
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<SubscriptionUsageCounter>> ListCountersAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken) =>
        await Counters(tenantId)
            .Find(Builders<SubscriptionUsageCounter>.Filter.And(
                Builders<SubscriptionUsageCounter>.Filter.Eq(
                    counter => counter.TenantId,
                    tenantId),
                Builders<SubscriptionUsageCounter>.Filter.Eq(
                    counter => counter.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageCounter>.Filter.Eq(
                    counter => counter.PeriodKey,
                    periodKey)))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryMarkThresholdNotifiedAsync(
        string tenantId,
        string counterId,
        int thresholdPercent,
        CancellationToken cancellationToken)
    {
        // The filter asserting the threshold is absent is what makes this a race the database
        // settles: every process attempts it, exactly one modifies a document.
        var filter = Builders<SubscriptionUsageCounter>.Filter.And(
            Builders<SubscriptionUsageCounter>.Filter.Eq(
                counter => counter.ItemId,
                counterId),
            Builders<SubscriptionUsageCounter>.Filter.Ne(
                counter => counter.NotifiedThresholds,
                null),
            Builders<SubscriptionUsageCounter>.Filter.Not(
                Builders<SubscriptionUsageCounter>.Filter.AnyEq(
                    counter => counter.NotifiedThresholds,
                    thresholdPercent)));

        var result = await Counters(tenantId).UpdateOneAsync(
            filter,
            Builders<SubscriptionUsageCounter>.Update.Push(
                counter => counter.NotifiedThresholds,
                thresholdPercent),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<SubscriptionUsageRecord?> FindRecordByKeyAsync(
        string tenantId,
        string subscriptionId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await Records(tenantId)
            .Find(Builders<SubscriptionUsageRecord>.Filter.And(
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.TenantId,
                    tenantId),
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.IdempotencyKey,
                    idempotencyKey)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(decimal Balance, long RecordCount)> SummariseLedgerAsync(
        string tenantId,
        string subscriptionId,
        string meterKey,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var records = await Records(tenantId)
            .Find(Builders<SubscriptionUsageRecord>.Filter.And(
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.TenantId,
                    tenantId),
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.SubscriptionId,
                    subscriptionId),
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.MeterKey,
                    meterKey),
                Builders<SubscriptionUsageRecord>.Filter.Eq(
                    record => record.PeriodKey,
                    periodKey)))
            .Project(record => record.Delta)
            .ToListAsync(cancellationToken);

        return (records.Sum(), records.Count);
    }

    public async Task<bool> TryRepairCounterAsync(
        string tenantId,
        string counterId,
        decimal balance,
        long appliedRecordCount,
        CancellationToken cancellationToken)
    {
        var result = await Counters(tenantId).UpdateOneAsync(
            Builders<SubscriptionUsageCounter>.Filter.And(
                Builders<SubscriptionUsageCounter>.Filter.Eq(
                    counter => counter.ItemId,
                    counterId),
                Builders<SubscriptionUsageCounter>.Filter.Lt(
                    counter => counter.AppliedRecordCount,
                    appliedRecordCount)),
            Builders<SubscriptionUsageCounter>.Update
                .Set(counter => counter.Balance, balance)
                .Set(counter => counter.AppliedRecordCount, appliedRecordCount)
                .Set(counter => counter.LastUpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<SubscriptionUsageRecord>> ListRecordsAsync(
        string tenantId,
        string subscriptionId,
        string? meterKey,
        string? periodKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionUsageRecord>.Filter.And(
            Builders<SubscriptionUsageRecord>.Filter.Eq(
                record => record.TenantId,
                tenantId),
            Builders<SubscriptionUsageRecord>.Filter.Eq(
                record => record.SubscriptionId,
                subscriptionId));

        if (!string.IsNullOrWhiteSpace(meterKey))
        {
            filter &= Builders<SubscriptionUsageRecord>.Filter.Eq(
                record => record.MeterKey,
                meterKey);
        }

        if (!string.IsNullOrWhiteSpace(periodKey))
        {
            filter &= Builders<SubscriptionUsageRecord>.Filter.Eq(
                record => record.PeriodKey,
                periodKey);
        }

        return await Records(tenantId)
            .Find(filter)
            .SortByDescending(record => record.OccurredAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    private IMongoCollection<SubscriptionUsageRecord> Records(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageRecord>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageRecords);

    private IMongoCollection<SubscriptionUsageCounter> Counters(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionUsageCounter>(
            _dbContextProvider,
            tenantId,
            SubscriptionCollections.UsageCounters);
}
