using System.Collections.Concurrent;
using Blocks.Genesis;
using MongoDB.Driver;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

public sealed class SubscriptionDiscountRepository : ISubscriptionDiscountRepository
{
    private readonly IDbContextProvider _db;
    private readonly ConcurrentDictionary<string, byte> _indexed = new();

    public SubscriptionDiscountRepository(IDbContextProvider db) => _db = db;

    public async Task<bool> TryCreateAsync(Discount discount, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureIndexesAsync(discount.TenantId, cancellationToken);
            await Collection(discount.TenantId).InsertOneAsync(discount, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<Discount?> FindActiveByCodeAsync(
        string tenantId, string? organizationId, string code, CancellationToken cancellationToken)
    {
        var baseFilter = Builders<Discount>.Filter.And(
            Builders<Discount>.Filter.Eq(item => item.TenantId, tenantId),
            Builders<Discount>.Filter.Eq(item => item.Code, code),
            Builders<Discount>.Filter.Eq(item => item.Status, CatalogueStatus.Active));

        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var owned = await Collection(tenantId).Find(Builders<Discount>.Filter.And(
                baseFilter,
                Builders<Discount>.Filter.Eq(item => item.OrganizationId, organizationId)))
                .FirstOrDefaultAsync(cancellationToken);
            if (owned is not null) return owned;
        }

        return await Collection(tenantId).Find(Builders<Discount>.Filter.And(
            baseFilter,
            Builders<Discount>.Filter.Eq(item => item.OrganizationId, null)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Discount>> ListAsync(
        string tenantId, string? organizationId, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(organizationId)
            ? Builders<Discount>.Filter.Eq(item => item.OrganizationId, null)
            : Builders<Discount>.Filter.In(item => item.OrganizationId, new string?[] { null, organizationId });
        return await Collection(tenantId).Find(Builders<Discount>.Filter.And(
                Builders<Discount>.Filter.Eq(item => item.TenantId, tenantId), scope))
            .SortBy(item => item.Code).ToListAsync(cancellationToken);
    }

    public async Task<bool> TryArchiveAsync(string tenantId, string discountId, CancellationToken cancellationToken)
    {
        var result = await Collection(tenantId).UpdateOneAsync(
            Builders<Discount>.Filter.And(
                Builders<Discount>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<Discount>.Filter.Eq(item => item.ItemId, discountId),
                Builders<Discount>.Filter.Eq(item => item.Status, CatalogueStatus.Active)),
            Builders<Discount>.Update
                .Set(item => item.Status, CatalogueStatus.Archived)
                .Set(item => item.LastUpdatedDateUtc, DateTime.UtcNow),
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public Task<Discount?> FindByIdAsync(
        string tenantId, string discountId, CancellationToken cancellationToken) =>
        Collection(tenantId).Find(Builders<Discount>.Filter.And(
                Builders<Discount>.Filter.Eq(item => item.TenantId, tenantId),
                Builders<Discount>.Filter.Eq(item => item.ItemId, discountId)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryUpdateAsync(
        Discount discount, long expectedVersion, CancellationToken cancellationToken)
    {
        discount.LastUpdatedDateUtc = DateTime.UtcNow;
        discount.Version = expectedVersion + 1;

        var result = await Collection(discount.TenantId).ReplaceOneAsync(
            Builders<Discount>.Filter.And(
                Builders<Discount>.Filter.Eq(item => item.TenantId, discount.TenantId),
                Builders<Discount>.Filter.Eq(item => item.ItemId, discount.ItemId),
                Builders<Discount>.Filter.Eq(item => item.Version, expectedVersion)),
            discount,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    private async Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_indexed.ContainsKey(tenantId)) return;
        await Collection(tenantId).Indexes.CreateManyAsync(
            SubscriptionIndexDefinitions.CreateDiscountIndexes(), cancellationToken);
        _indexed.TryAdd(tenantId, 0);
    }

    private IMongoCollection<Discount> Collection(string tenantId) =>
        SubscriptionCollections.Of<Discount>(_db, tenantId, SubscriptionCollections.Discounts);
}
