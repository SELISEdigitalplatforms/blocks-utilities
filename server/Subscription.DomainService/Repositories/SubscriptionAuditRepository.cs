using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Bson;
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
            // null, so reading one plan's history would otherwise scan the collection.
            //
            // Partial, not sparse. A sparse compound index includes a document when *any* indexed
            // field exists, and TenantId and OrganizationId exist on every audit event ever
            // written — so Sparse would have indexed the entire collection while reading as though
            // it excluded it. The filter names the two fields that actually distinguish an
            // aggregate event, so the index holds only those.
            new CreateIndexModel<SubscriptionAuditEvent>(
                Builders<SubscriptionAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OrganizationId)
                    .Ascending(x => x.AggregateType).Ascending(x => x.AggregateId)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions<SubscriptionAuditEvent>
                {
                    Name = "ix_subscription_audit_aggregate",
                    PartialFilterExpression = Builders<SubscriptionAuditEvent>.Filter.And(
                        Builders<SubscriptionAuditEvent>.Filter.Type(
                            x => x.AggregateType, BsonType.String),
                        Builders<SubscriptionAuditEvent>.Filter.Type(
                            x => x.AggregateId, BsonType.String))
                })
        ], cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    private IMongoCollection<SubscriptionAuditEvent> Events(string tenantId) =>
        SubscriptionCollections.Of<SubscriptionAuditEvent>(_db, tenantId, SubscriptionCollections.AuditEvents);
}
