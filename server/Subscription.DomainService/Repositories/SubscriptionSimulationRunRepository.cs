using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionSimulationRunRepository : ISubscriptionSimulationRunRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionSimulationRunRepository(IDbContextProvider db) => _db = db;

    public async Task AppendAsync(SubscriptionSimulationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        await EnsureIndexesAsync(run.TenantId, cancellationToken);
        await Runs(run.TenantId).InsertOneAsync(run, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionSimulationRun>> ListAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken) =>
        await Runs(tenantId)
            .Find(Builders<SubscriptionSimulationRun>.Filter.And(
                Builders<SubscriptionSimulationRun>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<SubscriptionSimulationRun>.Filter.Eq(x => x.OrganizationId, organizationId),
                Builders<SubscriptionSimulationRun>.Filter.Eq(x => x.SubscriptionId, subscriptionId)))
            .SortByDescending(x => x.StartedAtUtc)
            .Limit(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId)) return;

        await Runs(tenantId).Indexes.CreateManyAsync([
            new CreateIndexModel<SubscriptionSimulationRun>(
                Builders<SubscriptionSimulationRun>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OrganizationId)
                    .Ascending(x => x.SubscriptionId).Descending(x => x.StartedAtUtc),
                new CreateIndexOptions { Name = "ix_subscription_simulation_run_timeline" })
        ], cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionSimulationRun> Runs(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionSimulationRun>(
            _db, tenantId, SubscriptionCollections.SimulationRuns);
}
