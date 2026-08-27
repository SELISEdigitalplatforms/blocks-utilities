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

        await Closures(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateUsagePeriodClosureIndexes(),
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
            State = UsagePeriodClaimState.Active,
            UpdatedAtUtc = DateTime.UtcNow
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

    /// <summary>
    /// Releases a claim through a resumable three-step protocol — <c>Active</c> to
    /// <c>ReleasePending</c>, then the counter decrement, then <c>ReleasePending</c> to
    /// <c>Released</c> — so a crash between the decrement and the final state write leaves
    /// something a retry can find and finish, rather than a claim stuck forever short of
    /// <c>Released</c> with no record of whether its decrement ever actually landed.
    /// </summary>
    public async Task ReleaseClaimAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var claimId = UsagePeriodClaim.CreateId(subscriptionId, periodKey, idempotencyKey);

        var toPending = await Claims(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.ItemId, claimId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.State, UsagePeriodClaimState.Active)),
            Builders<UsagePeriodClaim>.Update
                .Set(claim => claim.State, UsagePeriodClaimState.ReleasePending)
                .Set(claim => claim.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (toPending.ModifiedCount != 1)
        {
            // Not Active. Either this is a genuinely fresh call against a claim already mid- or
            // fully-released — resume or no-op below — or the claim never actually existed (it
            // was rejected and deleted), which is a no-op too.
            var existing = await Claims(tenantId)
                .Find(Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.ItemId, claimId))
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is not { State: UsagePeriodClaimState.ReleasePending })
            {
                // Missing (rejected/never claimed) or already Released — nothing left to do.
                // Still Active is only reachable if another caller's CAS above raced this one and
                // won; that caller owns finishing the release.
                return;
            }

            // Found already in ReleasePending — a retry after a crash between the CAS above and
            // whatever came next. Resume from the decrement rather than treating this as done.
        }

        await ApplyDecrementIdempotentlyAsync(tenantId, subscriptionId, periodKey, claimId, cancellationToken);

        await Claims(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.ItemId, claimId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.State, UsagePeriodClaimState.ReleasePending)),
            Builders<UsagePeriodClaim>.Update
                .Set(claim => claim.State, UsagePeriodClaimState.Released)
                .Set(claim => claim.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        // Once the claim itself is terminal, retries return from that terminal state without
        // attempting the decrement again. The operation id is no longer needed on the hot period
        // document, so remove it instead of growing one unbounded array for every usage call.
        await Closures(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId,
                UsagePeriodClosure.CreateId(subscriptionId, periodKey)),
            Builders<UsagePeriodClosure>.Update.Pull(
                closure => closure.AppliedReleaseOperationIds,
                $"claim-release:{claimId}"),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Decrements <see cref="UsagePeriodClosure.ActiveWriterCount"/> exactly once per claim,
    /// however many times this is called for the same claim — a stable
    /// <c>claim-release:{claimId}</c> operation id tracked in
    /// <see cref="UsagePeriodClosure.AppliedReleaseOperationIds"/> makes a retried decrement (a
    /// crash or a lost acknowledgement after the write but before the caller learned of it) safe.
    /// </summary>
    private async Task ApplyDecrementIdempotentlyAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string claimId,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);
        var releaseOperationId = $"claim-release:{claimId}";

        var decremented = await Closures(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClosure>.Filter.And(
                Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId, closureId),
                Builders<UsagePeriodClosure>.Filter.AnyNin(
                    closure => closure.AppliedReleaseOperationIds, [releaseOperationId]),
                Builders<UsagePeriodClosure>.Filter.Gt(closure => closure.ActiveWriterCount, 0)),
            Builders<UsagePeriodClosure>.Update
                .Inc(closure => closure.ActiveWriterCount, -1)
                .AddToSet(closure => closure.AppliedReleaseOperationIds, releaseOperationId)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (decremented.ModifiedCount == 1)
        {
            return;
        }

        // Either this operation id was already applied (a retry — nothing more to do), or the
        // count was already at zero (should not happen in the ordinary protocol, but is not this
        // method's job to diagnose). Either way, the operation id itself must still end up
        // recorded, so a later retry of this same release never attempts a second decrement.
        await Closures(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClosure>.Filter.And(
                Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.ItemId, closureId),
                Builders<UsagePeriodClosure>.Filter.AnyNin(
                    closure => closure.AppliedReleaseOperationIds, [releaseOperationId])),
            Builders<UsagePeriodClosure>.Update
                .AddToSet(closure => closure.AppliedReleaseOperationIds, releaseOperationId)
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

    public async Task<ClosureCommitOutcome> TryCommitClosingAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string closeOperationId,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);

        var result = await Closures(tenantId).UpdateOneAsync(
            ReservedByFilter(closureId, closeOperationId),
            Builders<UsagePeriodClosure>.Update
                .Set(closure => closure.State, UsagePeriodClosureState.Closing)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return ClosureCommitOutcome.Committed;
        }

        var existing = await GetAsync(tenantId, subscriptionId, periodKey, cancellationToken);

        if (existing is null)
        {
            return ClosureCommitOutcome.NotFound;
        }

        if (existing.CloseOperationId != closeOperationId)
        {
            return ClosureCommitOutcome.OperationMismatch;
        }

        return existing.State is UsagePeriodClosureState.Closing or UsagePeriodClosureState.Closed
            ? ClosureCommitOutcome.AlreadyCommitted
            : ClosureCommitOutcome.StateConflict;
    }

    public async Task<ClosureReleaseOutcome> TryReleaseReservationAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        string closeOperationId,
        CancellationToken cancellationToken)
    {
        var closureId = UsagePeriodClosure.CreateId(subscriptionId, periodKey);

        var result = await Closures(tenantId).UpdateOneAsync(
            ReservedByFilter(closureId, closeOperationId),
            Builders<UsagePeriodClosure>.Update
                .Set(closure => closure.State, UsagePeriodClosureState.Open)
                .Set(closure => closure.EffectiveEndUtc, (DateTime?)null)
                .Set(closure => closure.CloseOperationId, (string?)null)
                .Set(closure => closure.ReservationCreatedAtUtc, (DateTime?)null)
                .Set(closure => closure.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);

        if (result.ModifiedCount == 1)
        {
            return ClosureReleaseOutcome.Released;
        }

        var existing = await GetAsync(tenantId, subscriptionId, periodKey, cancellationToken);

        if (existing is null)
        {
            return ClosureReleaseOutcome.NotFound;
        }

        if (existing.State == UsagePeriodClosureState.Open && existing.CloseOperationId is null)
        {
            return ClosureReleaseOutcome.AlreadyReleased;
        }

        return existing.CloseOperationId != closeOperationId
            ? ClosureReleaseOutcome.OperationMismatch
            : ClosureReleaseOutcome.StateConflict;
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

    public async Task<IReadOnlyList<UsagePeriodClosure>> ListStaleReservationsAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        return await Closures(tenantId)
            .Find(Builders<UsagePeriodClosure>.Filter.And(
                Builders<UsagePeriodClosure>.Filter.Eq(closure => closure.TenantId, tenantId),
                Builders<UsagePeriodClosure>.Filter.Eq(
                    closure => closure.State, UsagePeriodClosureState.CloseReserved),
                Builders<UsagePeriodClosure>.Filter.Lt(
                    closure => closure.ReservationCreatedAtUtc, olderThanUtc)))
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasOutstandingClaimsAsync(
        string tenantId,
        string subscriptionId,
        string periodKey,
        CancellationToken cancellationToken) =>
        await Claims(tenantId)
            .Find(Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.TenantId, tenantId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.SubscriptionId, subscriptionId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.PeriodKey, periodKey),
                Builders<UsagePeriodClaim>.Filter.In(
                    claim => claim.State,
                    [UsagePeriodClaimState.Active, UsagePeriodClaimState.ReleasePending])))
            .Limit(1)
            .AnyAsync(cancellationToken);

    public async Task<IReadOnlyList<UsagePeriodClaim>> ListStaleClaimsAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken) =>
        await Claims(tenantId)
            .Find(Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.TenantId, tenantId),
                Builders<UsagePeriodClaim>.Filter.In(
                    claim => claim.State,
                    [UsagePeriodClaimState.Active, UsagePeriodClaimState.ReleasePending]),
                Builders<UsagePeriodClaim>.Filter.Lte(claim => claim.UpdatedAtUtc, olderThanUtc)))
            .SortBy(claim => claim.UpdatedAtUtc)
            .Limit(Math.Max(1, limit))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryBeginStaleClaimRecoveryAsync(
        string tenantId,
        string claimId,
        DateTime olderThanUtc,
        CancellationToken cancellationToken)
    {
        var result = await Claims(tenantId).UpdateOneAsync(
            Builders<UsagePeriodClaim>.Filter.And(
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.ItemId, claimId),
                Builders<UsagePeriodClaim>.Filter.Eq(claim => claim.State, UsagePeriodClaimState.Active),
                Builders<UsagePeriodClaim>.Filter.Lte(claim => claim.UpdatedAtUtc, olderThanUtc)),
            Builders<UsagePeriodClaim>.Update
                .Set(claim => claim.State, UsagePeriodClaimState.ReleasePending)
                .Set(claim => claim.UpdatedAtUtc, DateTime.UtcNow),
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
