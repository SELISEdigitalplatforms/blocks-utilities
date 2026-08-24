using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The work queue, in the root database rather than any tenant's.
/// </summary>
/// <remarks>
/// Addressed by connection string and database name directly, the same door
/// <c>RootDatabaseTenantSource</c> uses: background work has no ambient tenant to resolve from, and
/// the whole point of this collection is to be readable across every tenant at once.
/// </remarks>
public sealed class SubscriptionWorkQueue : ISubscriptionWorkQueue
{
    private const string CollectionName = "SubscriptionBackgroundWork";
    private const string FallbackRootDatabase = "BlocksRootDb";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly TimeProvider _time;

    /// <summary>
    /// The index guarantee, held here rather than left to callers.
    /// </summary>
    /// <remarks>
    /// Every read and write below awaits this first, because a document written before the unique
    /// occurrence index exists can be a duplicate — and duplicates make the index un-creatable
    /// afterwards, so the hole holds itself open. A caller that simply forgot to call
    /// <see cref="EnsureIndexesAsync"/> would open it just as wide, which is why the collection
    /// cannot be reached without going through this.
    /// <para>
    /// Reset on failure so the next call retries rather than caching a broken outcome forever.
    /// </para>
    /// </remarks>
    private Task? _indexes;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    public SubscriptionWorkQueue(
        IDbContextProvider dbContextProvider,
        IBlocksSecret secret,
        TimeProvider? time = null)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
        _time = time ?? TimeProvider.System;
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        if (_indexes is { IsCompletedSuccessfully: true })
        {
            return;
        }

        await _indexGate.WaitAsync(cancellationToken);

        try
        {
            if (_indexes is { IsCompletedSuccessfully: true })
            {
                return;
            }

            _indexes = CreateIndexesAsync(cancellationToken);

            await _indexes;
        }
        catch
        {
            // Not cached: a transient failure must not make every later call believe the indexes
            // are permanently unavailable, and a permanent one must keep saying so.
            _indexes = null;

            throw;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var builder = Builders<SubscriptionBackgroundWork>.IndexKeys;

        await Work().Indexes.CreateManyAsync(
            [
                // The claim query. Status first because it is the most selective thing a claim
                // knows, then the instant it is comparing, then the order it wants results in.
                new CreateIndexModel<SubscriptionBackgroundWork>(
                    builder
                        .Ascending(work => work.Status)
                        .Ascending(work => work.NextAttemptAtUtc)
                        .Ascending(work => work.Priority),
                    new CreateIndexOptions { Name = SubscriptionWorkIndexNames.Due }),

                // Reclaiming after a worker dies. Without this, finding expired leases means a
                // collection scan at exactly the moment the queue is already behind.
                new CreateIndexModel<SubscriptionBackgroundWork>(
                    builder
                        .Ascending(work => work.Status)
                        .Ascending(work => work.LeaseExpiresAtUtc),
                    new CreateIndexOptions { Name = SubscriptionWorkIndexNames.ExpiredLeases }),

                // The one that carries a correctness guarantee rather than a speed one: two
                // producers scheduling the same occurrence land on one document.
                new CreateIndexModel<SubscriptionBackgroundWork>(
                    builder
                        .Ascending(work => work.TenantId)
                        .Ascending(work => work.WorkType)
                        .Ascending(work => work.AggregateId)
                        .Ascending(work => work.WorkKey),
                    new CreateIndexOptions
                    {
                        Name = SubscriptionWorkIndexNames.Occurrence,
                        Unique = true
                    }),

                // Diagnostics: everything scheduled for one tenant, or one subscription, in order.
                new CreateIndexModel<SubscriptionBackgroundWork>(
                    builder
                        .Ascending(work => work.TenantId)
                        .Ascending(work => work.AggregateId)
                        .Descending(work => work.CreatedAtUtc),
                    new CreateIndexOptions { Name = SubscriptionWorkIndexNames.Diagnostics }),

                // Retention for finished records only. PurgeAtUtc is null on anything pending,
                // processing or dead-lettered, and a TTL index ignores documents whose field is
                // absent or not a date — which is what keeps unfinished work from expiring.
                new CreateIndexModel<SubscriptionBackgroundWork>(
                    builder.Ascending(work => work.PurgeAtUtc),
                    new CreateIndexOptions
                    {
                        Name = SubscriptionWorkIndexNames.Purge,
                        ExpireAfter = TimeSpan.Zero
                    })
            ],
            cancellationToken);
    }

    public async Task<bool> ScheduleAsync(
        SubscriptionBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Before the first insert, always. A duplicate written now is one the unique index can
        // never be built over, so the cost of skipping this is not a duplicate job — it is a
        // collection that can never enforce uniqueness again.
        await EnsureIndexesAsync(cancellationToken);

        var now = _time.GetUtcNow().UtcDateTime;

        work.CreatedAtUtc = now;
        work.UpdatedAtUtc = now;
        work.Status = BackgroundWorkStatus.Pending;

        try
        {
            await Work().InsertOneAsync(work, cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The occurrence is already scheduled. Left exactly as it is: a pending item does not
            // need re-scheduling, and one that is processing must not have its lease disturbed by
            // a producer.
            return false;
        }
    }

    public async Task<IReadOnlyList<SubscriptionBackgroundWork>> ClaimDueAsync(
        string leaseId,
        string leasedBy,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);

        var claimed = new List<SubscriptionBackgroundWork>();

        for (var index = 0; index < Math.Max(1, batchSize); index++)
        {
            var item = await ClaimOneAsync(leaseId, leasedBy, leaseDuration, cancellationToken);

            if (item is null)
            {
                break;
            }

            claimed.Add(item);
        }

        return claimed;
    }

    /// <summary>
    /// One item, claimed atomically.
    /// </summary>
    /// <remarks>
    /// A single <c>FindOneAndUpdate</c> rather than a read followed by a write: two workers polling
    /// the same queue would otherwise both read the same document and both believe they own it.
    /// </remarks>
    private async Task<SubscriptionBackgroundWork?> ClaimOneAsync(
        string leaseId,
        string leasedBy,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var filter = Builders<SubscriptionBackgroundWork>.Filter.Or(
            // Due and nobody's.
            Builders<SubscriptionBackgroundWork>.Filter.And(
                Builders<SubscriptionBackgroundWork>.Filter.Eq(
                    work => work.Status,
                    BackgroundWorkStatus.Pending),
                Builders<SubscriptionBackgroundWork>.Filter.Lte(
                    work => work.NextAttemptAtUtc,
                    now)),
            // Or claimed by a worker that has stopped saying so. The lease, not the status, is what
            // says whether anyone is still on it.
            Builders<SubscriptionBackgroundWork>.Filter.And(
                Builders<SubscriptionBackgroundWork>.Filter.Eq(
                    work => work.Status,
                    BackgroundWorkStatus.Processing),
                Builders<SubscriptionBackgroundWork>.Filter.Lte(
                    work => work.LeaseExpiresAtUtc,
                    now)));

        var update = Builders<SubscriptionBackgroundWork>.Update
            .Set(work => work.Status, BackgroundWorkStatus.Processing)
            .Set(work => work.LeaseId, leaseId)
            .Set(work => work.LeasedBy, leasedBy)
            .Set(work => work.LeaseExpiresAtUtc, now.Add(leaseDuration))
            .Set(work => work.OperationId, Guid.NewGuid().ToString("N"))
            .Set(work => work.UpdatedAtUtc, now)
            .Inc(work => work.AttemptCount, 1);

        return await Work().FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<SubscriptionBackgroundWork>
            {
                // Priority first, then the longest overdue: a backlog of bookkeeping must not be
                // able to delay a renewal simply by being older.
                Sort = Builders<SubscriptionBackgroundWork>.Sort
                    .Ascending(work => work.Priority)
                    .Ascending(work => work.NextAttemptAtUtc),
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task<bool> RenewLeaseAsync(
        string itemId,
        string leaseId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var result = await Work().UpdateOneAsync(
            LeaseFilter(itemId, leaseId),
            Builders<SubscriptionBackgroundWork>.Update
                .Set(work => work.LeaseExpiresAtUtc, now.Add(leaseDuration))
                .Set(work => work.UpdatedAtUtc, now),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> CompleteAsync(
        string itemId,
        string leaseId,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var result = await Work().UpdateOneAsync(
            LeaseFilter(itemId, leaseId),
            Builders<SubscriptionBackgroundWork>.Update
                .Set(work => work.Status, BackgroundWorkStatus.Completed)
                .Set(work => work.CompletedAtUtc, now)
                // Only ever set here, which is what confines the TTL index to finished work.
                .Set(work => work.PurgeAtUtc, now.Add(retention))
                .Unset(work => work.LeaseId)
                .Unset(work => work.LeaseExpiresAtUtc)
                .Set(work => work.UpdatedAtUtc, now),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<BackgroundWorkStatus> FailAsync(
        string itemId,
        string leaseId,
        string errorCode,
        string errorMessage,
        bool permanent,
        TimeSpan backoff,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var current = await Work()
            .Find(LeaseFilter(itemId, leaseId))
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            // The lease moved on, so this attempt no longer speaks for the item.
            return BackgroundWorkStatus.Processing;
        }

        var exhausted = current.AttemptCount >= Math.Max(1, current.MaxAttempts);
        var status = permanent || exhausted
            ? BackgroundWorkStatus.DeadLetter
            : BackgroundWorkStatus.Pending;

        var update = Builders<SubscriptionBackgroundWork>.Update
            .Set(work => work.Status, status)
            .Set(work => work.LastErrorCode, errorCode)
            .Set(work => work.LastErrorMessage, errorMessage)
            .Unset(work => work.LeaseId)
            .Unset(work => work.LeaseExpiresAtUtc)
            .Set(work => work.UpdatedAtUtc, now);

        if (status == BackgroundWorkStatus.Pending)
        {
            update = update.Set(work => work.NextAttemptAtUtc, now.Add(backoff));
        }

        await Work().UpdateOneAsync(
            LeaseFilter(itemId, leaseId),
            update,
            cancellationToken: cancellationToken);

        return status;
    }

    public async Task<IReadOnlyList<SubscriptionBackgroundWork>> ListDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken,
        string? tenantId = null)
    {
        await EnsureIndexesAsync(cancellationToken);

        var filter = Builders<SubscriptionBackgroundWork>.Filter.Eq(
            work => work.Status,
            BackgroundWorkStatus.DeadLetter);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            filter = Builders<SubscriptionBackgroundWork>.Filter.And(filter, TenantFilter(tenantId));
        }

        return await Work()
            .Find(filter)
            .SortByDescending(work => work.UpdatedAtUtc)
            .Limit(Math.Max(1, limit))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionBackgroundWork?> GetAsync(
        string itemId,
        CancellationToken cancellationToken) =>
        await Work()
            .Find(Builders<SubscriptionBackgroundWork>.Filter.Eq(work => work.ItemId, itemId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryRequeueAsync(
        string itemId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var result = await Work().UpdateOneAsync(
            DeadLetteredFilter(itemId),
            Builders<SubscriptionBackgroundWork>.Update
                .Set(work => work.Status, BackgroundWorkStatus.Pending)
                // Reset together with the status, in the same write. Left at its ceiling, the item
                // would dead-letter again on its first failure and look like the requeue had not
                // worked.
                .Set(work => work.AttemptCount, 0)
                .Set(work => work.NextAttemptAtUtc, now)
                // Cleared for the same reason: an item with a stale lease is one no worker can
                // claim, which is indistinguishable from a requeue that silently did nothing.
                .Unset(work => work.LeaseId)
                .Unset(work => work.LeasedBy)
                .Unset(work => work.LeaseExpiresAtUtc)
                // Kept, not cleared: why it failed last time is what an operator watching the
                // retry needs, and it is overwritten by the next failure anyway.
                .Set(work => work.LastErrorMessage, $"Requeued: {reason}")
                .Set(work => work.UpdatedAtUtc, now),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryAbandonAsync(
        string itemId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var result = await Work().UpdateOneAsync(
            DeadLetteredFilter(itemId),
            Builders<SubscriptionBackgroundWork>.Update
                .Set(work => work.Status, BackgroundWorkStatus.Abandoned)
                .Set(work => work.LastErrorMessage, $"Abandoned: {reason}")
                .Set(work => work.UpdatedAtUtc, now)
                // Still no purge instant: the reason it was abandoned is part of the record.
                .Unset(work => work.LeaseId)
                .Unset(work => work.LeaseExpiresAtUtc),
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    /// <summary>
    /// One item, and only while it is dead-lettered.
    /// </summary>
    /// <remarks>
    /// The status is part of the filter so an operator acting on a stale list cannot reset the
    /// counters of an attempt that is already running, or undo another operator's decision made a
    /// second earlier.
    /// </remarks>
    private static FilterDefinition<SubscriptionBackgroundWork> DeadLetteredFilter(string itemId) =>
        Builders<SubscriptionBackgroundWork>.Filter.And(
            Builders<SubscriptionBackgroundWork>.Filter.Eq(work => work.ItemId, itemId),
            Builders<SubscriptionBackgroundWork>.Filter.Eq(
                work => work.Status,
                BackgroundWorkStatus.DeadLetter));

    private static FilterDefinition<SubscriptionBackgroundWork> TenantFilter(string tenantId) =>
        Builders<SubscriptionBackgroundWork>.Filter.Eq(work => work.TenantId, tenantId);

    public async Task<IReadOnlyList<SubscriptionWorkQueueDepth>> DescribeDepthAsync(
        CancellationToken cancellationToken)
    {
        var grouped = await Work()
            .Aggregate()
            .Match(Builders<SubscriptionBackgroundWork>.Filter.Ne(
                work => work.Status,
                BackgroundWorkStatus.Completed))
            .Group(
                work => new { work.WorkType, work.Status },
                group => new
                {
                    group.Key,
                    Count = group.LongCount(),
                    OldestDueAtUtc = group.Min(work => work.DueAtUtc)
                })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(entry => new SubscriptionWorkQueueDepth(
                entry.Key.WorkType,
                entry.Key.Status,
                entry.Count,
                entry.OldestDueAtUtc))
            .ToList();
    }

    private static FilterDefinition<SubscriptionBackgroundWork> LeaseFilter(
        string itemId,
        string leaseId) =>
        Builders<SubscriptionBackgroundWork>.Filter.And(
            Builders<SubscriptionBackgroundWork>.Filter.Eq(work => work.ItemId, itemId),
            // The lease, always: an attempt whose lease has been taken over must not be able to
            // complete or fail the item on the new holder's behalf.
            Builders<SubscriptionBackgroundWork>.Filter.Eq(work => work.LeaseId, leaseId));

    private IMongoCollection<SubscriptionBackgroundWork> Work()
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        return _dbContextProvider
            .GetDatabase(_secret.DatabaseConnectionString, rootDatabase)
            .GetCollection<SubscriptionBackgroundWork>(CollectionName);
    }
}

/// <summary>Named so a migration can find them and an operator can recognize them.</summary>
public static class SubscriptionWorkIndexNames
{
    public const string Due = "ix_subwork_status_next_attempt_priority";
    public const string ExpiredLeases = "ix_subwork_status_lease_expires";
    public const string Occurrence = "ux_subwork_tenant_type_aggregate_key";
    public const string Diagnostics = "ix_subwork_tenant_aggregate_created";
    public const string Purge = "ttl_subwork_purge_at";
}
