using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class UsagePeriodClosureRepository : IUsagePeriodClosureRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public UsagePeriodClosureRepository(IDbContextProvider dbContextProvider) =>
        _dbContextProvider = dbContextProvider;

    public async Task EnsureIndexesAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId))
        {
            return;
        }

        await Claims(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsagePeriodClaimIndexes(),
            cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    public async Task<UsageClaimOutcome> TryAcquireClaimAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var claim = new UsagePeriodClaim
        {
            ItemId = UsagePeriodClaim.CreateId(subscriptionId, periodKey, idempotencyKey),
            TenantId = tenantId,
            SubscriptionId = subscriptionId,
            PeriodKey = periodKey,
            IdempotencyKey = idempotencyKey,
            State = UsagePeriodClaimState.Active
        };

        bool freshlyAcquired;

        try
        {
            await Claims(tenantId).InsertOneAsync(claim, cancellationToken: cancellationToken);
            freshlyAcquired = true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A claim for this exact idempotency key already exists — a retry of the same
            // request. It already counted toward ActiveWriterCount the first time; counting it
            // again here would never let the period reach zero writers.
            freshlyAcquired = false;
        }

        if (!freshlyAcquired)
        {
            return UsageClaimOutcome.AlreadyClaimed;
        }

        if (await TryIncrementActiveWritersAsync(
                tenantId, subscriptionId, periodKey, occurredAtUtc, cancellationToken))
        {
            return UsageClaimOutcome.Acquired;
        }

        // The period is Closing, Closed, or the usage occurred at or after its boundary. Undo the
        // claim just taken out, so a rejected attempt never counts toward ActiveWriterCount and a
        // later ReleaseClaimAsync for this same key has nothing to reverse.
        await Claims(tenantId).DeleteOneAsync(
            Builders<UsagePeriodClaim>.Filter.Eq(existing => existing.ItemId, claim.ItemId),
            cancellationToken);

        return UsageClaimOutcome.Rejected;
    }

    public async Task ReleaseClaimAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var claimId = UsagePeriodClaim.CreateId(subscriptionId, periodKey, idempotencyKey);

        var result = await Claims(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.ItemId, claimId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.State, UsagePeriodClaimState.Active)),
            Builders<UsagePeriodClaim>.Update.Set(claim => claim.State, UsagePeriodClaimState.Released),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount != 1)
        {
            // Already released (a duplicate release call), or never actually acquired (the claim
            // was rejected and deleted) — either way, ActiveWriterCount has nothing to reverse.
            return;
        }

        await Closures(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClosure>.Filter.Eq(
                closure => closure.ItemId, UsagePeriodClosure.CreateId(subscriptionId, periodKey)),
            Builders<UsagePeriodClosure>.Update
                .Inc(closure => closure.ActiveWriterCount, -1)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    public async Task StartClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        DateTime effectiveEndUtc,
        string closeOperationId,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);

        var result = await Closures(tenantId).UpdateOneAsync(
            OpenFilter(closureId),
            ClosingUpdate(effectiveEndUtc, closeOperationId),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return;
        }

        try
        {
            await Closures(tenantId).InsertOneAsync(
                new UsagePeriodClosure
                {
                    ItemId = closureId,
                    TenantId = tenantId,
                    SubscriptionId = subscriptionId,
                    PeriodKey = periodKey,
                    State = UsagePeriodClosureState.Closing,
                    EffectiveEndUtc = effectiveEndUtc,
                    ActiveWriterCount = 0,
                    CloseOperationId = closeOperationId
                },
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Someone created it between our failed update and this insert — either still Open
            // (retry the conditional update once more) or already Closing/Closed, in which case
            // this call has nothing left to do: only one cancellation ever closes a given period,
            // so a document already past Open already carries this same boundary.
            await Closures(tenantId).UpdateOneAsync(
                OpenFilter(closureId),
                ClosingUpdate(effectiveEndUtc, closeOperationId),
                cancellationToken: cancellationToken);
        }
    }

    public async Task<UsagePeriodClosure?> GetAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken) =>
        await Closures(tenantId)
            .Find(Builders<UsagePeriodClosure>.Filter.Eq(
                closure => closure.ItemId, UsagePeriodClosure.CreateId(subscriptionId, periodKey)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryMarkClosedAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var result = await Closures(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClosure>.Filter.And(
                Builders<UsagePeriodClosure>.Filter.Eq(
                    closure => closure.ItemId, UsagePeriodClosure.CreateId(subscriptionId, periodKey)),
                Builders<UsagePeriodClosure>.Filter.Eq(
                    closure => closure.State, UsagePeriodClosureState.Closing)),
            Builders<UsagePeriodClosure>.Update
                .Set(closure => closure.State, UsagePeriodClosureState.Closed)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    /// <summary>
    /// The one place the conditional increment is attempted, so <see cref="TryAcquireClaimAsync"/>
    /// can retry it once after lazily creating a first-ever closure document without duplicating
    /// the filter/update pair.
    /// </summary>
    private async Task<bool> TryIncrementActiveWritersAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);

        var result = await Closures(tenantId).UpdateOneAsync(
            OpenAndBeforeBoundaryFilter(closureId, occurredAtUtc),
            Builders<UsagePeriodClosure>.Update
                .Inc(closure => closure.ActiveWriterCount, 1)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return true;
        }

        // No document yet for this period — the first usage ever recorded against it. Create it
        // Open, then retry: a concurrent first-writer race is resolved by ItemId's own uniqueness,
        // not by which caller's insert happens to win.
        try
        {
            await Closures(tenantId).InsertOneAsync(
                new UsagePeriodClosure
                {
                    ItemId = closureId,
                    TenantId = tenantId,
                    SubscriptionId = subscriptionId,
                    PeriodKey = periodKey,
                    State = UsagePeriodClosureState.Open,
                    ActiveWriterCount = 0
                },
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Someone else created it in the meantime — the retry below sees whatever state it
            // is actually in.
        }

        var retry = await Closures(tenantId).UpdateOneAsync(
            OpenAndBeforeBoundaryFilter(closureId, occurredAtUtc),
            Builders<UsagePeriodClosure>.Update
                .Inc(closure => closure.ActiveWriterCount, 1)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return retry.ModifiedCount == 1;
    }

    private static FilterDefinition<UsagePeriodClosure> OpenFilter(string closureId) =>
        Builders<UsagePeriodClosure>.Filter.And(
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId, closureId),
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.State, UsagePeriodClosureState.Open));

    private static FilterDefinition<UsagePeriodClosure> OpenAndBeforeBoundaryFilter(
        string closureId, DateTime occurredAtUtc) =>
        Builders<UsagePeriodClosure>.Filter.And(
            OpenFilter(closureId),
            Builders<UsagePeriodClosure>.Filter.Or(
                Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.EffectiveEndUtc, null),
                Builders<UsagePeriodClosure>.Filter.Gt(closure => closure.EffectiveEndUtc, occurredAtUtc)));

    private static UpdateDefinition<UsagePeriodClosure> ClosingUpdate(
        DateTime effectiveEndUtc, string closeOperationId) =>
        Builders<UsagePeriodClosure>.Update
            .Set(closure => closure.State, UsagePeriodClosureState.Closing)
            .Set(closure => closure.EffectiveEndUtc, effectiveEndUtc)
            .Set(closure => closure.CloseOperationId, closeOperationId)
            .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow);

    private IMongoCollection<UsagePeriodClosure> Closures(string tenantId) =>
        SubscriptionCollections.Of<UsagePeriodClosure>(
            _dbContextProvider, tenantId, SubscriptionCollections.UsagePeriodClosures);

    private IMongoCollection<UsagePeriodClaim> Claims(string tenantId) =>
        SubscriptionCollections.Of<UsagePeriodClaim>(
            _dbContextProvider, tenantId, SubscriptionCollections.UsagePeriodClaims);
}
