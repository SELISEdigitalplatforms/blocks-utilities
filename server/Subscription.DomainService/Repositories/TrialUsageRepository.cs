using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>The Mongo-backed ledger behind <see cref="ITrialUsageRepository"/>.</summary>
public sealed class TrialUsageRepository : ITrialUsageRepository
{
    private const int DuplicateKeyErrorCode = 11000;

    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public TrialUsageRepository(IDbContextProvider db) => _db = db;

    public async Task<TrialUsage?> FindActiveAsync(
        string tenantId,
        SubscriberScope scope,
        string subjectId,
        string planCode,
        CancellationToken cancellationToken) =>
        await Collection(tenantId).Find(Builders<TrialUsage>.Filter.And(
                Builders<TrialUsage>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<TrialUsage>.Filter.Eq(item => item.Scope, scope),
                Builders<TrialUsage>.Filter.Eq(item => item.SubjectId, subjectId),
                Builders<TrialUsage>.Filter.Eq(item => item.PlanCode, planCode),
                Builders<TrialUsage>.Filter.Lt(item => item.State, TrialUsageState.Released)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryReserveAsync(TrialUsage usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await EnsureIndexesAsync(usage.TenantId, cancellationToken);

        usage.State = TrialUsageState.Claimed;

        try
        {
            await Collection(usage.TenantId).InsertOneAsync(usage, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // Whose claim refused this is re-read rather than assumed: a retry of this very signup
            // loses the same way a stranger's does.
            var holder = await FindActiveAsync(
                usage.TenantId, usage.Scope, usage.SubjectId, usage.PlanCode, cancellationToken);

            return holder is not null &&
                string.Equals(holder.SubscriptionId, usage.SubscriptionId, StringComparison.Ordinal);
        }
    }

    public async Task MarkUsedAsync(
        TrialUsage usage, DateTime usedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await EnsureIndexesAsync(usage.TenantId, cancellationToken);

        var collection = Collection(usage.TenantId);
        var mine = Builders<TrialUsage>.Filter.And(
            Builders<TrialUsage>.Filter.Eq(item => item.TenantId, usage.TenantId),
            Builders<TrialUsage>.Filter.Eq(item => item.SubscriptionId, usage.SubscriptionId),
            Builders<TrialUsage>.Filter.Eq(item => item.PlanCode, usage.PlanCode));

        var promoted = await collection.UpdateOneAsync(
            Builders<TrialUsage>.Filter.And(
                mine,
                Builders<TrialUsage>.Filter.Eq(item => item.State, TrialUsageState.Claimed)),
            Builders<TrialUsage>.Update
                .Set(item => item.State, TrialUsageState.Used)
                .Set(item => item.UsedAtUtc, usedAtUtc),
            cancellationToken: cancellationToken);

        if (promoted.ModifiedCount > 0 ||
            await collection.Find(Builders<TrialUsage>.Filter.And(
                    mine,
                    Builders<TrialUsage>.Filter.Eq(item => item.State, TrialUsageState.Used)))
                .AnyAsync(cancellationToken))
        {
            return;
        }

        usage.State = TrialUsageState.Used;
        usage.UsedAtUtc = usedAtUtc;

        try
        {
            await collection.InsertOneAsync(usage, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // Another subscription already holds this plan's trial for this subscriber. That claim
            // is the record that matters; there is nothing to add.
        }
    }

    public async Task ReleaseAsync(
        string tenantId, string usageId, DateTime releasedAtUtc, CancellationToken cancellationToken) =>
        await Collection(tenantId).UpdateOneAsync(
            Builders<TrialUsage>.Filter.And(
                Builders<TrialUsage>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<TrialUsage>.Filter.Eq(item => item.ItemId, usageId),
                Builders<TrialUsage>.Filter.Eq(item => item.State, TrialUsageState.Claimed)),
            Builders<TrialUsage>.Update
                .Set(item => item.State, TrialUsageState.Released)
                .Set(item => item.ReleasedAtUtc, releasedAtUtc),
            cancellationToken: cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            TrialUsageIndexDefinitions.CreateIndexes(), cancellationToken);
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

    private IMongoCollection<TrialUsage> Collection(string tenantId) =>
        SubscriptionCollections.Of<TrialUsage>(_db, tenantId, SubscriptionCollections.TrialUsages);
}
