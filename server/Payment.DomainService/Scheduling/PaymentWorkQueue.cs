using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// The queue of payment background work, in the root database rather than any tenant's.
/// </summary>
/// <remarks>
/// Addressed by connection string and database name directly: background work has no ambient tenant
/// to resolve from, and the point of this collection is to be readable across every tenant at once.
/// <para>
/// Its own collection, not one shared with subscription work. The two modules keep their own index
/// creation deliberately — <c>PaymentIndexDefinitions</c> and its subscription counterpart document
/// why — and a shared work collection would put both modules' indexes, work-type versioning and
/// deployment back in one place.
/// </para>
/// </remarks>
public sealed class PaymentWorkQueue : IPaymentWorkQueue
{
    private const string CollectionName = "PaymentBackgroundWork";
    private const string FallbackRootDatabase = "BlocksRootDb";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly TimeProvider _time;

    /// <summary>
    /// The index guarantee, held here rather than left to callers.
    /// </summary>
    /// <remarks>
    /// Every read and write awaits this first. A document written before the unique occurrence index
    /// exists can be a duplicate, and duplicates make the index un-creatable afterwards — so the gap
    /// would hold itself open, and a caller that merely forgot to create indexes would open it just
    /// as wide.
    /// </remarks>
    private Task? _indexes;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    public PaymentWorkQueue(
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
            // Not cached: a transient failure must not make every later call believe the indexes are
            // permanently unavailable.
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
        var builder = Builders<PaymentBackgroundWork>.IndexKeys;

        await Work().Indexes.CreateManyAsync(
            [
                new CreateIndexModel<PaymentBackgroundWork>(
                    builder
                        .Ascending(work => work.Status)
                        .Ascending(work => work.NextAttemptAtUtc)
                        .Ascending(work => work.Priority),
                    new CreateIndexOptions { Name = PaymentWorkIndexNames.Due }),

                new CreateIndexModel<PaymentBackgroundWork>(
                    builder
                        .Ascending(work => work.Status)
                        .Ascending(work => work.LeaseExpiresAtUtc),
                    new CreateIndexOptions { Name = PaymentWorkIndexNames.ExpiredLeases }),

                // The one that carries a correctness guarantee rather than a speed one.
                new CreateIndexModel<PaymentBackgroundWork>(
                    builder
                        .Ascending(work => work.TenantId)
                        .Ascending(work => work.WorkType)
                        .Ascending(work => work.AggregateId)
                        .Ascending(work => work.WorkKey),
                    new CreateIndexOptions
                    {
                        Name = PaymentWorkIndexNames.Occurrence,
                        Unique = true
                    }),

                new CreateIndexModel<PaymentBackgroundWork>(
                    builder
                        .Ascending(work => work.TenantId)
                        .Ascending(work => work.AggregateId)
                        .Descending(work => work.CreatedAtUtc),
                    new CreateIndexOptions { Name = PaymentWorkIndexNames.Diagnostics }),

                // Retention for finished records only: PurgeAtUtc is null on anything pending,
                // processing, dead-lettered or abandoned, and a TTL index ignores absent fields.
                new CreateIndexModel<PaymentBackgroundWork>(
                    builder.Ascending(work => work.PurgeAtUtc),
                    new CreateIndexOptions
                    {
                        Name = PaymentWorkIndexNames.Purge,
                        ExpireAfter = TimeSpan.Zero
                    })
            ],
            cancellationToken);
    }

    public async Task<bool> ScheduleAsync(
        PaymentBackgroundWork work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

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
            // Already scheduled, and left exactly as it is: a pending item needs no rescheduling,
            // and one that is processing must not have its lease disturbed by a producer.
            return false;
        }
    }

    public async Task<IReadOnlyList<PaymentBackgroundWork>> ClaimDueAsync(
        string leaseId,
        string leasedBy,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);

        var claimed = new List<PaymentBackgroundWork>();

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
    /// A single <c>FindOneAndUpdate</c> rather than a read then a write: two workers polling the
    /// same queue would otherwise both read the same document and both believe they own it.
    /// </remarks>
    private async Task<PaymentBackgroundWork?> ClaimOneAsync(
        string leaseId,
        string leasedBy,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var filter = Builders<PaymentBackgroundWork>.Filter.Or(
            Builders<PaymentBackgroundWork>.Filter.And(
                Builders<PaymentBackgroundWork>.Filter.Eq(
                    work => work.Status,
                    BackgroundWorkStatus.Pending),
                Builders<PaymentBackgroundWork>.Filter.Lte(work => work.NextAttemptAtUtc, now)),
            // The lease, not the status, says whether anyone is still on it.
            Builders<PaymentBackgroundWork>.Filter.And(
                Builders<PaymentBackgroundWork>.Filter.Eq(
                    work => work.Status,
                    BackgroundWorkStatus.Processing),
                Builders<PaymentBackgroundWork>.Filter.Lte(work => work.LeaseExpiresAtUtc, now)));

        return await Work().FindOneAndUpdateAsync(
            filter,
            Builders<PaymentBackgroundWork>.Update
                .Set(work => work.Status, BackgroundWorkStatus.Processing)
                .Set(work => work.LeaseId, leaseId)
                .Set(work => work.LeasedBy, leasedBy)
                .Set(work => work.LeaseExpiresAtUtc, now.Add(leaseDuration))
                .Set(work => work.OperationId, Guid.NewGuid().ToString("N"))
                .Set(work => work.UpdatedAtUtc, now)
                .Inc(work => work.AttemptCount, 1),
            new FindOneAndUpdateOptions<PaymentBackgroundWork>
            {
                // Priority first, then longest overdue: a backlog of outbox events must not be able
                // to delay recovering a payment simply by being older.
                Sort = Builders<PaymentBackgroundWork>.Sort
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
            Builders<PaymentBackgroundWork>.Update
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
            Builders<PaymentBackgroundWork>.Update
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

        var update = Builders<PaymentBackgroundWork>.Update
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

    public async Task<IReadOnlyList<PaymentBackgroundWork>> ListDeadLetteredAsync(
        int limit,
        CancellationToken cancellationToken,
        string? tenantId = null)
    {
        await EnsureIndexesAsync(cancellationToken);

        var filter = Builders<PaymentBackgroundWork>.Filter.Eq(
            work => work.Status,
            BackgroundWorkStatus.DeadLetter);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            filter = Builders<PaymentBackgroundWork>.Filter.And(
                filter,
                Builders<PaymentBackgroundWork>.Filter.Eq(work => work.TenantId, tenantId));
        }

        return await Work()
            .Find(filter)
            .SortByDescending(work => work.UpdatedAtUtc)
            .Limit(Math.Max(1, limit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentWorkQueueDepth>> DescribeDepthAsync(
        CancellationToken cancellationToken)
    {
        var grouped = await Work()
            .Aggregate()
            .Match(Builders<PaymentBackgroundWork>.Filter.Ne(
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
            .Select(entry => new PaymentWorkQueueDepth(
                entry.Key.WorkType,
                entry.Key.Status,
                entry.Count,
                entry.OldestDueAtUtc))
            .ToList();
    }

    private static FilterDefinition<PaymentBackgroundWork> LeaseFilter(
        string itemId,
        string leaseId) =>
        Builders<PaymentBackgroundWork>.Filter.And(
            Builders<PaymentBackgroundWork>.Filter.Eq(work => work.ItemId, itemId),
            // The lease, always: an attempt whose lease has been taken over must not be able to
            // complete or fail the item on the new holder's behalf.
            Builders<PaymentBackgroundWork>.Filter.Eq(work => work.LeaseId, leaseId));

    private IMongoCollection<PaymentBackgroundWork> Work()
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        return _dbContextProvider
            .GetDatabase(_secret.DatabaseConnectionString, rootDatabase)
            .GetCollection<PaymentBackgroundWork>(CollectionName);
    }
}

/// <summary>Named so a migration can find them and an operator can recognize them.</summary>
public static class PaymentWorkIndexNames
{
    public const string Due = "ix_paywork_status_next_attempt_priority";
    public const string ExpiredLeases = "ix_paywork_status_lease_expires";
    public const string Occurrence = "ux_paywork_tenant_type_aggregate_key";
    public const string Diagnostics = "ix_paywork_tenant_aggregate_created";
    public const string Purge = "ttl_paywork_purge_at";
}
