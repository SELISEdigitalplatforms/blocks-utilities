using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Utility.DomainService.Drive.Models;

namespace Utility.DomainService.Drive.Service;

/// <summary>
/// Mongo persistence for drive connections and OAuth states.
/// </summary>
/// <remarks>
/// Both collections live in the root tenant database and are isolated by <c>ProjectKey</c>, the
/// same arrangement MagicLink uses: the callback arrives from Entra with no tenant header, so the
/// state has to be findable without one.
/// <para>
/// Indexes are created once per process on first use. Mongo treats an identical
/// <c>createIndexes</c> as a no-op, so several replicas doing this is harmless.
/// </para>
/// </remarks>
public sealed class DriveRepository : IDriveRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly IConfiguration _configuration;
    private Task? _indexes;

    public DriveRepository(IDbContextProvider dbContextProvider, IConfiguration configuration)
    {
        _dbContextProvider = dbContextProvider;
        _configuration = configuration;
    }

    public async Task<DriveConnection?> GetConnectionAsync(string projectKey, string userId, CancellationToken cancellationToken)
    {
        var connections = await ConnectionsAsync(cancellationToken);
        return await connections
            .Find(ConnectionFilter(projectKey, userId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertConnectionAsync(DriveConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var connections = await ConnectionsAsync(cancellationToken);
        await connections.ReplaceOneAsync(
            ConnectionFilter(connection.ProjectKey, connection.UserId),
            connection,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<bool> DeleteConnectionAsync(string projectKey, string userId, CancellationToken cancellationToken)
    {
        var connections = await ConnectionsAsync(cancellationToken);
        var result = await connections.DeleteOneAsync(ConnectionFilter(projectKey, userId), cancellationToken);
        return result.DeletedCount > 0;
    }

    public async Task TouchConnectionAsync(string projectKey, string userId, DateTime lastUsedAt, CancellationToken cancellationToken)
    {
        var connections = await ConnectionsAsync(cancellationToken);
        await connections.UpdateOneAsync(
            ConnectionFilter(projectKey, userId),
            Builders<DriveConnection>.Update.Set(x => x.LastUsedAt, lastUsedAt),
            cancellationToken: cancellationToken);
    }

    public async Task InsertStateAsync(DriveOAuthState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var states = await StatesAsync(cancellationToken);
        await states.InsertOneAsync(state, cancellationToken: cancellationToken);
    }

    public async Task<DriveOAuthState?> TakeStateAsync(string stateId, CancellationToken cancellationToken)
    {
        var states = await StatesAsync(cancellationToken);
        return await states.FindOneAndDeleteAsync(
            Builders<DriveOAuthState>.Filter.Eq(x => x.ItemId, stateId),
            cancellationToken: cancellationToken);
    }

    private static FilterDefinition<DriveConnection> ConnectionFilter(string projectKey, string userId) =>
        Builders<DriveConnection>.Filter.Eq(x => x.ProjectKey, projectKey)
        & Builders<DriveConnection>.Filter.Eq(x => x.UserId, userId);

    private async Task<IMongoCollection<DriveConnection>> ConnectionsAsync(CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);
        return Collection<DriveConnection>(DriveConnection.CollectionName);
    }

    private async Task<IMongoCollection<DriveOAuthState>> StatesAsync(CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken);
        return Collection<DriveOAuthState>(DriveOAuthState.CollectionName);
    }

    private IMongoCollection<T> Collection<T>(string name) =>
        _dbContextProvider.GetCollection<T>(_configuration["RootTenantId"] ?? string.Empty, name);

    private Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        // One shared task: whoever gets here first creates the indexes, everyone else awaits the
        // same attempt. A failed attempt is cleared so the next call retries instead of caching it.
        var pending = _indexes;
        if (pending is not null && !pending.IsFaulted)
        {
            return pending;
        }

        pending = CreateIndexesAsync(cancellationToken);
        _indexes = pending;
        return pending;
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        await Collection<DriveConnection>(DriveConnection.CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<DriveConnection>(
                Builders<DriveConnection>.IndexKeys.Ascending(x => x.ProjectKey).Ascending(x => x.UserId),
                new CreateIndexOptions { Unique = true, Name = "project_user_unique" }),
            cancellationToken: cancellationToken);

        await Collection<DriveOAuthState>(DriveOAuthState.CollectionName).Indexes.CreateOneAsync(
            new CreateIndexModel<DriveOAuthState>(
                Builders<DriveOAuthState>.IndexKeys.Ascending(x => x.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "expires_ttl" }),
            cancellationToken: cancellationToken);
    }
}
