using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

public sealed class MeterCatalogueRepository : IMeterCatalogueRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public MeterCatalogueRepository(IDbContextProvider db) => _db = db;

    public async Task<bool> TryCreateAsync(MeterCatalogueEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureIndexesAsync(entry.TenantId, cancellationToken);

        try
        {
            await Collection(entry.TenantId).InsertOneAsync(entry, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<MeterCatalogueEntry?> ResolveAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken) =>
        (string.IsNullOrWhiteSpace(organizationId)
            ? null
            : await FindAtScopeAsync(tenantId, organizationId, meterKey, cancellationToken))
        ?? await FindAtScopeAsync(tenantId, null, meterKey, cancellationToken);

    public Task<MeterCatalogueEntry?> FindAtScopeAsync(
        string tenantId, string? organizationId, string meterKey, CancellationToken cancellationToken)
    {
        var filter = Builders<MeterCatalogueEntry>.Filter;

        return Collection(tenantId)
            .Find(filter.And(
                filter.Eq(entry => entry.TenantId, tenantId),
                filter.Eq(entry => entry.OrganizationId, string.IsNullOrWhiteSpace(organizationId) ? null : organizationId),
                filter.Eq(entry => entry.MeterKey, meterKey)))
            .FirstOrDefaultAsync(cancellationToken)!;
    }

    public async Task<IReadOnlyList<MeterCatalogueEntry>> ListAsync(
        string tenantId, string? organizationId, CancellationToken cancellationToken)
    {
        var filter = Builders<MeterCatalogueEntry>.Filter;
        var scope = string.IsNullOrWhiteSpace(organizationId)
            ? filter.Eq(entry => entry.OrganizationId, null)
            : filter.In(entry => entry.OrganizationId, new string?[] { null, organizationId });

        return await Collection(tenantId)
            .Find(filter.And(filter.Eq(entry => entry.TenantId, tenantId), scope))
            .SortBy(entry => entry.MeterKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryUpdateAsync(
        MeterCatalogueEntry entry, long expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Version = expectedVersion + 1;

        var filter = Builders<MeterCatalogueEntry>.Filter;
        var result = await Collection(entry.TenantId).ReplaceOneAsync(
            filter.And(
                filter.Eq(item => item.TenantId, entry.TenantId),
                filter.Eq(item => item.ItemId, entry.ItemId),
                filter.Eq(item => item.Version, expectedVersion)),
            entry,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionCreditIndexDefinitions.CreateMeterCatalogueIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<MeterCatalogueEntry> Collection(string tenantId) =>
        SubscriptionCollections.Of<MeterCatalogueEntry>(_db, tenantId, SubscriptionCollections.MeterCatalogue);
}
