using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionAuditRepository : ISubscriptionAuditRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public SubscriptionAuditRepository(IDbContextProvider db) => _db = db;

    public async Task AppendAsync(SubscriptionAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await EnsureIndexesAsync(auditEvent.TenantId, cancellationToken);
        await Events(auditEvent.TenantId).InsertOneAsync(auditEvent, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionAuditEvent>> ListAsync(
        string tenantId,
        string organizationId,
        string subscriptionId,
        int limit,
        CancellationToken cancellationToken) =>
        await Events(tenantId)
            .Find(Builders<SubscriptionAuditEvent>.Filter.And(
                Builders<SubscriptionAuditEvent>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<SubscriptionAuditEvent>.Filter.Eq(x => x.OrganizationId, organizationId),
                Builders<SubscriptionAuditEvent>.Filter.Eq(x => x.SubscriptionId, subscriptionId)))
            .SortByDescending(x => x.OccurredAtUtc)
            .Limit(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId)) return;

        await Events(tenantId).Indexes.CreateManyAsync([
            new CreateIndexModel<SubscriptionAuditEvent>(
                Builders<SubscriptionAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OrganizationId)
                    .Ascending(x => x.SubscriptionId).Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "ix_subscription_audit_timeline" }),
            new CreateIndexModel<SubscriptionAuditEvent>(
                Builders<SubscriptionAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OperationId)
                    .Ascending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "ix_subscription_audit_operation" }),

            // The timeline index above leads on SubscriptionId, which a catalogue event leaves
            // null, so reading one plan's history would otherwise scan the collection. Sparse
            // because only the events that name an aggregate belong in it — every subscription
            // event ever written has all three fields null and has no reason to be indexed here.
            new CreateIndexModel<SubscriptionAuditEvent>(
                Builders<SubscriptionAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OrganizationId)
                    .Ascending(x => x.AggregateType).Ascending(x => x.AggregateId)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions
                {
                    Name = "ix_subscription_audit_aggregate",
                    Sparse = true
                })
        ], cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionAuditEvent> Events(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionAuditEvent>(_db, tenantId, SubscriptionCollections.AuditEvents);
}
