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

    public async Task<ClosureReservationOutcome> TryReserveClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        DateTime effectiveEndUtc,
        string closeOperationId,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);

        var existing = await GetAsync(tenantId, subscriptionId, periodKey, cancellationToken);

        if (existing is not null && existing.State != UsagePeriodClosureState.Open)
        {
            // Already reserved/closing/closed. Idempotent only under this exact operation —
            // otherwise this is a genuinely different outcome (a different boundary), and this
            // caller must not proceed as though it had reserved anything.
            return existing.CloseOperationId == closeOperationId
                ? ClosureReservationOutcome.Reserved
                : ClosureReservationOutcome.ConflictingOperation;
        }

        var result = await Closures(tenantId).UpdateOneAsync(
            OpenFilter(closureId),
            ReservedUpdate(effectiveEndUtc, closeOperationId),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return ClosureReservationOutcome.Reserved;
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
                    State = UsagePeriodClosureState.CloseReserved,
                    EffectiveEndUtc = effectiveEndUtc,
                    ActiveWriterCount = 0,
                    CloseOperationId = closeOperationId,
                    ReservationCreatedAtUtc = DateTime.UtcNow
                },
                cancellationToken: cancellationToken);

            return ClosureReservationOutcome.Reserved;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Someone else's write landed between our read and this insert. Re-read once more to
            // learn who actually has it, rather than assuming either outcome.
            var after = await GetAsync(tenantId, subscriptionId, periodKey, cancellationToken);

            return after?.CloseOperationId == closeOperationId
                ? ClosureReservationOutcome.Reserved
                : ClosureReservationOutcome.ConflictingOperation;
        }
    }

    public async Task TryCommitClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string closeOperationId,
        CancellationToken cancellationToken) =>
        await Closures(tenantId).UpdateOneAsync(
            ReservedByFilter(
                UsagePeriodClosure.CreateId(subscriptionId, periodKey), closeOperationId),
            Builders<UsagePeriodClosure>.Update
                .Set(closure => closure.State, UsagePeriodClosureState.Closing)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

    public async Task TryReleaseReservationAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string closeOperationId,
        CancellationToken cancellationToken) =>
        await Closures(tenantId).UpdateOneAsync(
            ReservedByFilter(
                UsagePeriodClosure.CreateId(subscriptionId, periodKey), closeOperationId),
            Builders<UsagePeriodClosure>.Update
                .Set(closure => closure.State, UsagePeriodClosureState.Open)
                .Set(closure => closure.EffectiveEndUtc, (DateTime?)null)
                .Set(closure => closure.CloseOperationId, (string?)null)
                .Set(closure => closure.ReservationCreatedAtUtc, (DateTime?)null)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

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

    /// <summary>
    /// A claim may be granted under <c>Open</c> or <c>CloseReserved</c> alike — a reservation on
    /// its own does not stop new usage, only a genuinely different reservation attempt. What stops
    /// new claims is the boundary itself, once one is set, and <c>Closing</c>/<c>Closed</c> once
    /// the cancellation that reserved this period actually commits.
    /// </summary>
    private static FilterDefinition<UsagePeriodClosure> OpenAndBeforeBoundaryFilter(
        string closureId, DateTime occurredAtUtc) =>
        Builders<UsagePeriodClosure>.Filter.And(
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId, closureId),
            Builders<UsagePeriodClosure>.Filter.In(
                closure => closure.State,
                [UsagePeriodClosureState.Open, UsagePeriodClosureState.CloseReserved]),
            Builders<UsagePeriodClosure>.Filter.Or(
                Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.EffectiveEndUtc, null),
                Builders<UsagePeriodClosure>.Filter.Gt(closure => closure.EffectiveEndUtc, occurredAtUtc)));

    private static FilterDefinition<UsagePeriodClosure> ReservedByFilter(
        string closureId, string closeOperationId) =>
        Builders<UsagePeriodClosure>.Filter.And(
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId, closureId),
            Builders<UsagePeriodClosure>.Filter.Eq(
                closure => closure.State, UsagePeriodClosureState.CloseReserved),
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.CloseOperationId, closeOperationId));

    private static UpdateDefinition<UsagePeriodClosure> ReservedUpdate(
        DateTime effectiveEndUtc, string closeOperationId) =>
        Builders<UsagePeriodClosure>.Update
            .Set(closure => closure.State, UsagePeriodClosureState.CloseReserved)
            .Set(closure => closure.EffectiveEndUtc, effectiveEndUtc)
            .Set(closure => closure.CloseOperationId, closeOperationId)
            .Set(closure => closure.ReservationCreatedAtUtc, DateTime.UtcNow)
            .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow);

    private IMongoCollection<UsagePeriodClosure> Closures(string tenantId) =>
        SubscriptionCollections.Of<UsagePeriodClosure>(
            _dbContextProvider, tenantId, SubscriptionCollections.UsagePeriodClosures);

    private IMongoCollection<UsagePeriodClaim> Claims(string tenantId) =>
        SubscriptionCollections.Of<UsagePeriodClaim>(
            _dbContextProvider, tenantId, SubscriptionCollections.UsagePeriodClaims);
}
