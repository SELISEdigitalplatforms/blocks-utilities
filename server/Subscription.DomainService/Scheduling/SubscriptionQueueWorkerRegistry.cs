using Blocks.Genesis;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// Which drainers are alive, readable from any process.
/// </summary>
public interface ISubscriptionQueueWorkerRegistry
{
    Task EnsureIndexesAsync(CancellationToken cancellationToken);

    /// <summary>Records this replica's state. Called from the drainer's own loop.</summary>
    Task HeartbeatAsync(
        SubscriptionQueueWorkerBeat beat,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the fleet looks like, judged against the database's clock rather than the caller's.
    /// </summary>
    /// <remarks>
    /// The clock matters. A readiness check comparing a Worker's timestamp against its own wall clock
    /// is measuring the skew between two machines as well as the age of the heartbeat, and in the
    /// direction that reports a live fleet as dead.
    /// </remarks>
    Task<SubscriptionQueueFleetHealth> DescribeFleetAsync(
        TimeSpan livenessWindow,
        TimeSpan claimWindow,
        CancellationToken cancellationToken);
}

/// <summary>One replica's report of itself, as the drainer knows it.</summary>
public sealed record SubscriptionQueueWorkerBeat(
    string WorkerId,
    DateTime StartedAtUtc,
    DateTime? LastClaimSucceededAtUtc,
    DateTime? LastBatchProcessedAtUtc,
    int ConsecutiveFailures,
    DateTime? LastFailureAtUtc,
    string? LastFailureClassification);

/// <summary>
/// The registry, in the root database beside the queue itself.
/// </summary>
/// <remarks>
/// Beside the queue deliberately: the thing a reader wants to know is whether anything can drain
/// <em>that</em> collection, and a heartbeat written somewhere else could be arriving from a process
/// that has lost the database the work is in.
/// </remarks>
public sealed class SubscriptionQueueWorkerRegistry : ISubscriptionQueueWorkerRegistry
{
    private const string CollectionName = "SubscriptionQueueWorkers";
    private const string FallbackRootDatabase = "BlocksRootDb";
    private const string LivenessIndexName = "ix_subworker_heartbeat";
    private const string PurgeIndexName = "ttl_subworker_expires_at";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly IOptionsMonitor<SubscriptionOptions> _options;

    private Task? _indexes;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    public SubscriptionQueueWorkerRegistry(
        IDbContextProvider dbContextProvider,
        IBlocksSecret secret,
        IOptionsMonitor<SubscriptionOptions> options)
    {
        _dbContextProvider = dbContextProvider;
        _secret = secret;
        _options = options;
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
            _indexes = null;

            throw;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    public async Task HeartbeatAsync(
        SubscriptionQueueWorkerBeat beat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beat);

        await EnsureIndexesAsync(cancellationToken);

        // The database's clock, not this process's. Every replica's heartbeat is then comparable
        // against every other's and against the reader's judgement of "recent", without a clock skew
        // between two pods deciding whether the fleet looks alive.
        var now = await NowAsync(cancellationToken);
        var retention = TimeSpan.FromSeconds(Math.Max(
            60,
            _options.CurrentValue.SchedulerWorkerRetentionSeconds));

        var update = Builders<SubscriptionQueueWorker>.Update
            .SetOnInsert(worker => worker.WorkerId, beat.WorkerId)
            .SetOnInsert(worker => worker.StartedAtUtc, beat.StartedAtUtc)
            .Set(worker => worker.HeartbeatAtUtc, now)
            .Set(worker => worker.ConsecutiveFailures, beat.ConsecutiveFailures)
            .Set(worker => worker.LastFailureAtUtc, beat.LastFailureAtUtc)
            .Set(worker => worker.LastFailureClassification, beat.LastFailureClassification)
            .Set(worker => worker.ExpiresAtUtc, now.Add(retention));

        // Only advanced when the drainer actually managed it, so a replica that starts failing keeps
        // the stamp of the last time it worked rather than having it refreshed by the heartbeat.
        if (beat.LastClaimSucceededAtUtc is not null)
        {
            update = update.Set(worker => worker.LastClaimSucceededAtUtc, now);
        }

        if (beat.LastBatchProcessedAtUtc is not null)
        {
            update = update.Set(worker => worker.LastBatchProcessedAtUtc, now);
        }

        await Workers().UpdateOneAsync(
            Builders<SubscriptionQueueWorker>.Filter.Eq(
                worker => worker.WorkerId,
                beat.WorkerId),
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<SubscriptionQueueFleetHealth> DescribeFleetAsync(
        TimeSpan livenessWindow,
        TimeSpan claimWindow,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);

        var now = await NowAsync(cancellationToken);
        var liveSince = now.Subtract(livenessWindow);

        var live = await Workers()
            .Find(Builders<SubscriptionQueueWorker>.Filter.Gte(
                worker => worker.HeartbeatAtUtc,
                liveSince))
            .ToListAsync(cancellationToken);

        if (live.Count == 0)
        {
            // Deliberately not "no news is good news". Zero live replicas is the state that used to
            // report healthy, and it is the state in which nothing is billed.
            return new SubscriptionQueueFleetHealth(0, 0, null, null, 0, null);
        }

        var claimSince = now.Subtract(claimWindow);

        var draining = live.Count(worker =>
            worker.LastClaimSucceededAtUtc is { } claimed && claimed >= claimSince);

        var worst = live.Max(worker => worker.ConsecutiveFailures);

        return new SubscriptionQueueFleetHealth(
            live.Count,
            draining,
            live.Max(worker => worker.HeartbeatAtUtc),
            live
                .Where(worker => worker.LastClaimSucceededAtUtc is not null)
                .Select(worker => worker.LastClaimSucceededAtUtc!.Value)
                .DefaultIfEmpty()
                .Max() is { Ticks: > 0 } newest
                    ? newest
                    : null,
            worst,
            live
                .OrderByDescending(worker => worker.LastFailureAtUtc ?? DateTime.MinValue)
                .Select(worker => worker.LastFailureClassification)
                .FirstOrDefault(classification => !string.IsNullOrWhiteSpace(classification)));
    }

    /// <summary>
    /// The database's own clock.
    /// </summary>
    /// <remarks>
    /// One admin command, and worth it: heartbeats written by several pods and judged by another
    /// process are only comparable if they all come from the same clock. Falls back to this process's
    /// clock rather than failing, because a readiness check that cannot answer is worse than one
    /// answering with a few seconds of skew.
    /// </remarks>
    private async Task<DateTime> NowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var database = Database();
            var result = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("hostInfo", 1),
                cancellationToken: cancellationToken);

            if (result.TryGetValue("system", out var system) &&
                system is BsonDocument info &&
                info.TryGetValue("currentTime", out var currentTime) &&
                currentTime.IsValidDateTime)
            {
                return currentTime.ToUniversalTime();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Not every deployment grants hostInfo. Skew between pods is a smaller problem than a
            // readiness endpoint that throws.
        }

        return DateTime.UtcNow;
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken) =>
        await Workers().Indexes.CreateManyAsync(
            [
                // The readiness query: every replica whose heartbeat is recent.
                new CreateIndexModel<SubscriptionQueueWorker>(
                    Builders<SubscriptionQueueWorker>.IndexKeys.Ascending(
                        worker => worker.HeartbeatAtUtc),
                    new CreateIndexOptions { Name = LivenessIndexName }),

                // Records of replicas that are gone. A TTL rather than a delete on shutdown, because
                // a killed pod never gets to tidy up and a registry that only removed records
                // politely would fill with the ones that crashed.
                new CreateIndexModel<SubscriptionQueueWorker>(
                    Builders<SubscriptionQueueWorker>.IndexKeys.Ascending(
                        worker => worker.ExpiresAtUtc),
                    new CreateIndexOptions
                    {
                        Name = PurgeIndexName,
                        ExpireAfter = TimeSpan.Zero
                    })
            ],
            cancellationToken);

    private IMongoDatabase Database() =>
        _dbContextProvider.GetDatabase(
            _secret.DatabaseConnectionString,
            string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
                ? FallbackRootDatabase
                : _secret.RootDatabaseName);

    private IMongoCollection<SubscriptionQueueWorker> Workers() =>
        Database().GetCollection<SubscriptionQueueWorker>(CollectionName);
}
