using Blocks.Genesis;
using MongoDB.Driver;
using Sms.DomainService.Utilities;

namespace Sms.DomainService.Scheduling;

public interface ISmsWorkQueue
{
    Task ScheduleAsync(string tenantId, string messageId, string correlationId, SmsWorkKind kind, DateTime dueAtUtc, CancellationToken cancellationToken = default);
    Task<SmsBackgroundWork?> ClaimNextAsync(CancellationToken cancellationToken = default);
    Task CompleteAsync(SmsBackgroundWork work, CancellationToken cancellationToken = default);
    Task FailAsync(SmsBackgroundWork work, string reason, CancellationToken cancellationToken = default);

    /// <summary>Drops a pending occurrence that no longer has anything to do. A leased one is left to finish.</summary>
    Task CancelAsync(string tenantId, string messageId, SmsWorkKind kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// The SMS slice of the Subscription work-queue pattern: a collection in the root database,
/// claimed one item at a time with a single FindOneAndUpdate so two workers never take the same
/// item, and a lease that lapses if the worker dies.
/// </summary>
public sealed class SmsWorkQueue : ISmsWorkQueue
{
    public const string CollectionName = "SmsBackgroundWork";
    private const string FallbackRootDatabase = "BlocksRootDb";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    // ponytail: fixed handler-failure budget and backoff; move to options if operators need to tune it.
    private const int MaxHandlerFailures = 10;

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private bool _indexesReady;

    public SmsWorkQueue(IDbContextProvider dbContextProvider, IBlocksSecret secret, TimeProvider? time = null)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
        _time = time ?? TimeProvider.System;
    }

    public async Task ScheduleAsync(string tenantId, string messageId, string correlationId, SmsWorkKind kind, DateTime dueAtUtc, CancellationToken cancellationToken = default)
    {
        var work = await WorkAsync(cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;

        var update = Builders<SmsBackgroundWork>.Update;

        // A caller without the correlation id (a watchdog scheduled before the message is read) must
        // not blank the one an earlier schedule recorded.
        var correlation = string.IsNullOrWhiteSpace(correlationId)
            ? update.SetOnInsert(x => x.CorrelationId, string.Empty)
            : update.Set(x => x.CorrelationId, correlationId);

        await work.UpdateOneAsync(
            x => x.TenantId == tenantId && x.MessageId == messageId && x.Kind == kind,
            update.Combine(
                correlation,
                update.SetOnInsert(x => x.ItemId, Guid.NewGuid().ToString("N")),
                update.SetOnInsert(x => x.CreatedAtUtc, now))
                .Set(x => x.Status, SmsWorkStatus.Pending)
                .Set(x => x.DueAtUtc, dueAtUtc)
                .Set(x => x.LeaseId, null)
                .Set(x => x.LeaseExpiresAtUtc, null)
                .Set(x => x.UpdatedAtUtc, now),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<SmsBackgroundWork?> ClaimNextAsync(CancellationToken cancellationToken = default)
    {
        var work = await WorkAsync(cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        var filter = Builders<SmsBackgroundWork>.Filter;

        return await work.FindOneAndUpdateAsync(
            filter.Lte(x => x.DueAtUtc, now) & (
                filter.Eq(x => x.Status, SmsWorkStatus.Pending) |
                (filter.Eq(x => x.Status, SmsWorkStatus.Leased) & filter.Lt(x => x.LeaseExpiresAtUtc, now))),
            Builders<SmsBackgroundWork>.Update
                .Set(x => x.Status, SmsWorkStatus.Leased)
                .Set(x => x.LeaseId, Guid.NewGuid().ToString("N"))
                .Set(x => x.LeaseExpiresAtUtc, now.Add(LeaseDuration))
                .Set(x => x.UpdatedAtUtc, now),
            new FindOneAndUpdateOptions<SmsBackgroundWork>
            {
                Sort = Builders<SmsBackgroundWork>.Sort.Ascending(x => x.DueAtUtc),
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task CompleteAsync(SmsBackgroundWork work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var collection = await WorkAsync(cancellationToken);

        // Matched on the lease: if the handler rescheduled this same item while running, the
        // reschedule cleared the lease and the fresh occurrence survives.
        await collection.DeleteOneAsync(x => x.ItemId == work.ItemId && x.LeaseId == work.LeaseId, cancellationToken);
    }

    public async Task FailAsync(SmsBackgroundWork work, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        var collection = await WorkAsync(cancellationToken);
        var now = _time.GetUtcNow().UtcDateTime;
        var failures = work.FailureCount + 1;
        var backoff = TimeSpan.FromSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Min(failures, 6))));

        await collection.UpdateOneAsync(
            x => x.ItemId == work.ItemId && x.LeaseId == work.LeaseId,
            Builders<SmsBackgroundWork>.Update
                .Set(x => x.Status, failures >= MaxHandlerFailures ? SmsWorkStatus.Dead : SmsWorkStatus.Pending)
                .Set(x => x.FailureCount, failures)
                .Set(x => x.DueAtUtc, now.Add(backoff))
                .Set(x => x.LeaseId, null)
                .Set(x => x.LeaseExpiresAtUtc, null)
                .Set(x => x.LastError, SmsLogSanitizer.SanitizeError(reason))
                .Set(x => x.UpdatedAtUtc, now),
            cancellationToken: cancellationToken);
    }

    public async Task CancelAsync(string tenantId, string messageId, SmsWorkKind kind, CancellationToken cancellationToken = default)
    {
        var work = await WorkAsync(cancellationToken);
        await work.DeleteOneAsync(
            x => x.TenantId == tenantId && x.MessageId == messageId && x.Kind == kind && x.Status == SmsWorkStatus.Pending,
            cancellationToken);
    }

    private async Task<IMongoCollection<SmsBackgroundWork>> WorkAsync(CancellationToken cancellationToken)
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        var collection = _dbContextProvider
            .GetDatabase(_secret.DatabaseConnectionString, rootDatabase)
            .GetCollection<SmsBackgroundWork>(CollectionName);

        if (!_indexesReady)
        {
            await EnsureIndexesAsync(collection, cancellationToken);
        }

        return collection;
    }

    // Before any write: a duplicate written ahead of the unique index would make it un-creatable.
    private async Task EnsureIndexesAsync(IMongoCollection<SmsBackgroundWork> collection, CancellationToken cancellationToken)
    {
        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexesReady)
            {
                return;
            }

            var keys = Builders<SmsBackgroundWork>.IndexKeys;
            await collection.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<SmsBackgroundWork>(
                    keys.Ascending(x => x.TenantId).Ascending(x => x.MessageId).Ascending(x => x.Kind),
                    new CreateIndexOptions { Name = "ux_smswork_tenant_message_kind", Unique = true }),
                new CreateIndexModel<SmsBackgroundWork>(
                    keys.Ascending(x => x.Status).Ascending(x => x.DueAtUtc),
                    new CreateIndexOptions { Name = "ix_smswork_status_due" })
            ], cancellationToken);

            _indexesReady = true;
        }
        finally
        {
            _indexGate.Release();
        }
    }
}
