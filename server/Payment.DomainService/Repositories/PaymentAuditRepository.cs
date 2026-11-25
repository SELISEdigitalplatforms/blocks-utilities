using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;

namespace Payment.DomainService.Repositories;

public sealed class PaymentAuditRepository : IPaymentAuditRepository
{
    private const string CollectionName = "PaymentAuditEvents";

    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexedTenants = new();

    public PaymentAuditRepository(IDbContextProvider db) => _db = db;

    public async Task AppendAsync(PaymentAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await EnsureIndexesAsync(auditEvent.TenantId, cancellationToken);
        await Events(auditEvent.TenantId).InsertOneAsync(auditEvent, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentAuditEvent>> ListAsync(
        string tenantId,
        string? organizationId,
        string? subjectType,
        string? subjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(tenantId, cancellationToken);

        var filter = Builders<PaymentAuditEvent>.Filter;
        var match = filter.Eq(x => x.TenantId, tenantId);

        if (organizationId is not null)
        {
            match &= filter.Eq(x => x.OrganizationId, organizationId);
        }

        if (!string.IsNullOrWhiteSpace(subjectType))
        {
            match &= filter.Eq(x => x.SubjectType, subjectType);
        }

        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            match &= filter.Eq(x => x.SubjectId, subjectId);
        }

        return await Events(tenantId)
            .Find(match)
            .SortByDescending(x => x.OccurredAtUtc)
            .Limit(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexedTenants.ContainsKey(tenantId)) return;

        await Events(tenantId).Indexes.CreateManyAsync([
            new CreateIndexModel<PaymentAuditEvent>(
                Builders<PaymentAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.SubjectType)
                    .Ascending(x => x.SubjectId).Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "ix_payment_audit_subject" }),
            new CreateIndexModel<PaymentAuditEvent>(
                Builders<PaymentAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Ascending(x => x.OrganizationId)
                    .Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "ix_payment_audit_organization" }),

            // The console's unfiltered list: tenant, newest first. Without it the sort runs in
            // memory over every event in the tenant and fails once that passes MongoDB's limit.
            new CreateIndexModel<PaymentAuditEvent>(
                Builders<PaymentAuditEvent>.IndexKeys
                    .Ascending(x => x.TenantId).Descending(x => x.OccurredAtUtc),
                new CreateIndexOptions { Name = "ix_payment_audit_tenant_recent" })
        ], cancellationToken);

        _indexedTenants.TryAdd(tenantId, 0);
    }

    private IMongoCollection<PaymentAuditEvent> Events(string tenantId) =>
        _db.GetDatabase(RequireTenant(tenantId)).GetCollection<PaymentAuditEvent>(CollectionName);

    private static string RequireTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId)
            ? tenantId
            : throw new InvalidOperationException("A tenant id is required for payment persistence.");
}
