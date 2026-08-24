using Blocks.Genesis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The mode record and the replica roster, in the root database.
/// </summary>
/// <remarks>
/// The same door <see cref="SubscriptionWorkQueue"/> uses, and for the same reason: this is fleet
/// state, so it cannot live in any one tenant's database.
/// <para>
/// Every timestamp that a decision depends on is written by the database rather than by the process
/// writing it. Replicas compare each other's heartbeats, and comparing timestamps written by
/// different machines' clocks is how a replica that is still working comes to look expired.
/// </para>
/// </remarks>
public sealed class SubscriptionSchedulerCoordinator : ISubscriptionSchedulerCoordinator
{
    private const string ModeCollectionName = "SubscriptionSchedulerMode";
    private const string ReplicaCollectionName = "SubscriptionSchedulerReplicas";
    private const string FallbackRootDatabase = "BlocksRootDb";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IBlocksSecret _secret;
    private readonly TimeProvider _time;

    private Task? _indexes;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    public SubscriptionSchedulerCoordinator(
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
            _indexes ??= CreateIndexesAsync(cancellationToken);

            await _indexes;
        }
        catch
        {
            // Cleared so the next call retries rather than caching a broken outcome forever.
            _indexes = null;

            throw;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    public async Task<SchedulerFleetView> ReadFleetAsync(
        TimeSpan replicaExpiry,
        CancellationToken cancellationToken)
    {
        var record = await Modes()
            .Find(mode => mode.Id == SubscriptionSchedulerModeRecord.SingletonId)
            .FirstOrDefaultAsync(cancellationToken);

        // Liveness decided by the database's clock, not this process's: $$NOW is evaluated where the
        // heartbeats were written, so the two sides of the comparison come from one clock.
        var live = new BsonDocument("$expr", new BsonDocument(
            "$gte",
            new BsonArray
            {
                "$HeartbeatAtUtc",
                new BsonDocument("$subtract", new BsonArray
                {
                    "$$NOW",
                    (long)replicaExpiry.TotalMilliseconds
                })
            }));

        var replicas = await Replicas()
            .Find(new BsonDocumentFilterDefinition<SubscriptionSchedulerReplica>(live))
            .ToListAsync(cancellationToken);

        return new SchedulerFleetView(record, replicas);
    }

    public async Task<bool> TrySeedAsync(
        SchedulerRunMode mode,
        string workerName,
        CancellationToken cancellationToken)
    {
        try
        {
            await Modes().InsertOneAsync(
                new SubscriptionSchedulerModeRecord
                {
                    Id = SubscriptionSchedulerModeRecord.SingletonId,
                    DesiredMode = mode,
                    // One rather than zero, so "never coordinated" and "coordinated once" are
                    // different values to a replica that has only ever seen its own default.
                    Generation = 1,
                    ProposedBy = workerName,
                    ProposedAtUtc = _time.GetUtcNow().UtcDateTime
                },
                cancellationToken: cancellationToken);

            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another replica seeded it first, which is the expected outcome for all but one member
            // of a fleet starting at once. Its record is as good as ours would have been.
            return false;
        }
    }

    public async Task<bool> TryProposeAsync(
        SchedulerRunMode mode,
        long expectedGeneration,
        string workerName,
        CancellationToken cancellationToken)
    {
        var filter = Builders<SubscriptionSchedulerModeRecord>.Filter.And(
            Builders<SubscriptionSchedulerModeRecord>.Filter.Eq(
                record => record.Id, SubscriptionSchedulerModeRecord.SingletonId),
            // Both conditions matter. The generation makes two simultaneous proposals collapse into
            // one, and the mode makes a proposal for the mode already in force a no-op rather than a
            // generation the whole fleet has to drain for.
            Builders<SubscriptionSchedulerModeRecord>.Filter.Eq(
                record => record.Generation, expectedGeneration),
            Builders<SubscriptionSchedulerModeRecord>.Filter.Ne(
                record => record.DesiredMode, mode));

        var update = Builders<SubscriptionSchedulerModeRecord>.Update
            .Set(record => record.DesiredMode, mode)
            .Set(record => record.Generation, expectedGeneration + 1)
            .Set(record => record.ProposedBy, workerName)
            .Set(record => record.ProposedAtUtc, _time.GetUtcNow().UtcDateTime);

        var result = await Modes().UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task ReportAsync(
        string workerName,
        SchedulerRunMode configuredMode,
        SchedulerRunMode activeMode,
        long generation,
        SchedulerReplicaState state,
        CancellationToken cancellationToken)
    {
        var update = Builders<SubscriptionSchedulerReplica>.Update
            .Set(replica => replica.ConfiguredMode, configuredMode)
            .Set(replica => replica.ActiveMode, activeMode)
            .Set(replica => replica.Generation, generation)
            .Set(replica => replica.State, state)
            // $currentDate: the heartbeat every other replica's liveness decision rests on is
            // stamped by the database, so a process with a wrong clock cannot claim to be newer or
            // older than it is.
            .CurrentDate(replica => replica.HeartbeatAtUtc)
            .SetOnInsert(replica => replica.StartedAtUtc, _time.GetUtcNow().UtcDateTime);

        await Replicas().UpdateOneAsync(
            replica => replica.WorkerName == workerName,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task RemoveAsync(string workerName, CancellationToken cancellationToken) =>
        await Replicas().DeleteOneAsync(
            replica => replica.WorkerName == workerName,
            cancellationToken);

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var replicas = Replicas();

        // One index, doing two jobs. It serves the liveness query, and its expiry stops the roster
        // accumulating a row for every pod that ever existed. A second index on the same key would
        // not be redundant but rejected outright — Mongo refuses an equivalent index under another
        // name — so asking for both is how neither gets created.
        //
        // The expiry is a day, not the liveness window: correctness rests on the heartbeat
        // comparison in ReadFleetAsync, which does not wait for a TTL monitor to run, and a row that
        // survives a while after its pod is useful to whoever is asking what used to run here.
        await replicas.Indexes.CreateOneAsync(
            new CreateIndexModel<SubscriptionSchedulerReplica>(
                Builders<SubscriptionSchedulerReplica>.IndexKeys
                    .Ascending(replica => replica.HeartbeatAtUtc),
                new CreateIndexOptions
                {
                    Name = SubscriptionSchedulerCoordinationIndexNames.ReplicaHeartbeat,
                    ExpireAfter = TimeSpan.FromDays(1)
                }),
            cancellationToken: cancellationToken);
    }

    private IMongoCollection<SubscriptionSchedulerModeRecord> Modes() =>
        RootDatabase().GetCollection<SubscriptionSchedulerModeRecord>(ModeCollectionName);

    private IMongoCollection<SubscriptionSchedulerReplica> Replicas() =>
        RootDatabase().GetCollection<SubscriptionSchedulerReplica>(ReplicaCollectionName);

    private IMongoDatabase RootDatabase()
    {
        var rootDatabase = string.IsNullOrWhiteSpace(_secret.RootDatabaseName)
            ? FallbackRootDatabase
            : _secret.RootDatabaseName;

        return _dbContextProvider.GetDatabase(_secret.DatabaseConnectionString, rootDatabase);
    }
}
