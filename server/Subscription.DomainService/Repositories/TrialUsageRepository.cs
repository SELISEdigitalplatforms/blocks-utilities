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
        CancellationToken cancellationToken)
    {
        // Indexed first because that also rekeys buyer claims: read before it, a colleague in an
        // organization that already used the trial would find nothing under the organization's key.
        await EnsureIndexesAsync(tenantId, cancellationToken);

        return await Collection(tenantId).Find(Builders<TrialUsage>.Filter.And(
                Builders<TrialUsage>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<TrialUsage>.Filter.Eq(item => item.Scope, scope),
                Builders<TrialUsage>.Filter.Eq(item => item.SubjectId, subjectId),
                Builders<TrialUsage>.Filter.Eq(item => item.PlanCode, planCode),
                Builders<TrialUsage>.Filter.Lt(item => item.State, TrialUsageState.Released)))
            .FirstOrDefaultAsync(cancellationToken);
    }

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
        var collection = Collection(tenantId);
        await collection.Indexes.CreateManyAsync(
            TrialUsageIndexDefinitions.CreateIndexes(), cancellationToken);
        await RekeyBuyerClaimsAsync(collection, tenantId, cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    /// <summary>
    /// Moves user-wise claims written when trials belonged to the buyer onto their organization.
    /// </summary>
    /// <remarks>
    /// Several colleagues may each hold a claim for the same organization and plan, and the unique
    /// index allows only one under the organization's key. So one is moved, a Used claim before a
    /// Claimed one and the earliest first, and the rest are left on their buyer's key: valid there,
    /// never looked up again, and kept, since claims are never deleted. Nothing is moved for an
    /// organization that already has a claim under its own key.
    /// <para>
    /// Idempotent, and safe to race: the winner is chosen the same way everywhere, the update only
    /// matches while the row still holds the buyer's key, and a signup that claimed under the
    /// organization's key in the meantime wins the duplicate key, which is what it should do.
    /// </para>
    /// </remarks>
    private static async Task RekeyBuyerClaimsAsync(
        IMongoCollection<TrialUsage> collection,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // ponytail: whole user-wise ledger read per tenant per process, fine while trial claims number in the thousands.
        var userWise = await collection.Find(Builders<TrialUsage>.Filter.And(
                Builders<TrialUsage>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<TrialUsage>.Filter.Eq(item => item.Scope, SubscriberScope.User),
                Builders<TrialUsage>.Filter.Lt(item => item.State, TrialUsageState.Released)))
            .ToListAsync(cancellationToken);

        foreach (var claims in userWise.GroupBy(item => (item.OrganizationId, item.PlanCode)))
        {
            var organizationId = claims.Key.OrganizationId;
            if (string.IsNullOrEmpty(organizationId) ||
                claims.Any(item => string.Equals(item.SubjectId, organizationId, StringComparison.Ordinal)))
            {
                continue;
            }

            var winner = claims
                .OrderByDescending(item => item.State)
                .ThenBy(item => item.UsedAtUtc ?? item.ClaimedAtUtc)
                .ThenBy(item => item.ItemId, StringComparer.Ordinal)
                .First();

            try
            {
                await collection.UpdateOneAsync(
                    Builders<TrialUsage>.Filter.And(
                        Builders<TrialUsage>.Filter.Eq(item => item.ItemId, winner.ItemId),
                        Builders<TrialUsage>.Filter.Eq(item => item.SubjectId, winner.SubjectId)),
                    Builders<TrialUsage>.Update.Set(item => item.SubjectId, organizationId),
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (IsDuplicateKey(exception))
            {
                // A signup claimed under the organization's key since the read. Its claim stands.
            }
        }
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
